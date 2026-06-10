using System.Buffers.Binary;
using System.Security.Cryptography;
using AdoMcpBridge.Core.Abstractions;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace AdoMcpBridge.Core.KeyVault;

/// <summary>
/// Envelope encryption: the plaintext is encrypted locally with a fresh
/// AES-256-GCM key, and only that 32-byte key is sent to Key Vault to be
/// wrapped with the DEK (RSA-OAEP-256). Direct RSA encryption cannot hold
/// payloads over ~318 bytes with a 3072-bit key, and Entra refresh tokens
/// are roughly 2 KB.
/// Blob layout: [1 version][2 wrapped-key length LE][wrapped key][12 nonce][16 tag][ciphertext].
/// </summary>
public sealed class KeyVaultEncryptor : IKeyVaultEncryptor
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 3; // version + wrapped-key length

    private readonly CryptographyClient _client;

    public KeyVaultEncryptor(CryptographyClient client) => _client = client;

    public async ValueTask<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct)
    {
        var aesKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            using (var gcm = new AesGcm(aesKey, TagSize))
            {
                gcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            var wrap = await _client.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, aesKey, ct);
            var wrapped = wrap.EncryptedKey;

            var blob = new byte[HeaderSize + wrapped.Length + NonceSize + TagSize + ciphertext.Length];
            blob[0] = Version;
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(1, 2), checked((ushort)wrapped.Length));
            var offset = HeaderSize;
            wrapped.CopyTo(blob.AsSpan(offset)); offset += wrapped.Length;
            nonce.CopyTo(blob.AsSpan(offset)); offset += NonceSize;
            tag.CopyTo(blob.AsSpan(offset)); offset += TagSize;
            ciphertext.CopyTo(blob.AsSpan(offset));
            return blob;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    public async ValueTask<byte[]> DecryptAsync(byte[] ciphertext, CancellationToken ct)
    {
        if (ciphertext.Length < HeaderSize || ciphertext[0] != Version)
        {
            throw new InvalidOperationException("Unrecognised encrypted-token envelope.");
        }

        var wrappedLength = BinaryPrimitives.ReadUInt16LittleEndian(ciphertext.AsSpan(1, 2));
        if (ciphertext.Length < HeaderSize + wrappedLength + NonceSize + TagSize)
        {
            throw new InvalidOperationException("Unrecognised encrypted-token envelope.");
        }

        var offset = HeaderSize;
        var wrapped = ciphertext.AsSpan(offset, wrappedLength).ToArray(); offset += wrappedLength;
        var nonce = ciphertext.AsSpan(offset, NonceSize).ToArray(); offset += NonceSize;
        var tag = ciphertext.AsSpan(offset, TagSize).ToArray(); offset += TagSize;
        var body = ciphertext.AsSpan(offset).ToArray();

        var unwrap = await _client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrapped, ct);
        var aesKey = unwrap.Key;
        try
        {
            var plaintext = new byte[body.Length];
            using var gcm = new AesGcm(aesKey, TagSize);
            gcm.Decrypt(nonce, body, tag, plaintext);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }
}
