using System.Security.Cryptography;
using AdoMcpBridge.Core.KeyVault;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.KeyVault;

public sealed class KeyVaultEncryptorTests
{
    // Fake KV wrap/unwrap: an involution (XOR 0xFF) so unwrap(wrap(k)) == k
    // without real RSA. The encryptor must only ever send the small AES key
    // to Key Vault — never the plaintext.
    private static byte[] Xor(byte[] input)
    {
        var output = new byte[input.Length];
        for (var i = 0; i < input.Length; i++) output[i] = (byte)(input[i] ^ 0xFF);
        return output;
    }

    private static CryptographyClient FakeKv(Action<byte[]>? onWrap = null)
    {
        var crypto = Substitute.For<CryptographyClient>();
        crypto.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var key = (byte[])ci[1];
                onWrap?.Invoke(key);
                return CryptographyModelFactory.WrapResult(
                    keyId: "kid", key: Xor(key), algorithm: KeyWrapAlgorithm.RsaOaep256);
            });
        crypto.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(ci => CryptographyModelFactory.UnwrapResult(
                keyId: "kid", key: Xor((byte[])ci[1]), algorithm: KeyWrapAlgorithm.RsaOaep256));
        return crypto;
    }

    [Fact]
    public async Task Round_trips_payloads_larger_than_RSA_could_ever_encrypt()
    {
        // Entra refresh tokens are ~2 KB; RSA-3072 OAEP-256 caps out at 318 bytes.
        byte[]? wrappedKey = null;
        var encryptor = new KeyVaultEncryptor(FakeKv(k => wrappedKey = k));
        var plaintext = RandomNumberGenerator.GetBytes(2048);

        var blob = await encryptor.EncryptAsync(plaintext, CancellationToken.None);
        var decrypted = await encryptor.DecryptAsync(blob, CancellationToken.None);

        Assert.Equal(plaintext, decrypted);
        Assert.NotNull(wrappedKey);
        Assert.Equal(32, wrappedKey!.Length); // only the AES-256 key goes to KV
    }

    [Fact]
    public async Task Each_encryption_uses_a_fresh_key_and_nonce()
    {
        var encryptor = new KeyVaultEncryptor(FakeKv());
        var plaintext = RandomNumberGenerator.GetBytes(64);

        var blob1 = await encryptor.EncryptAsync(plaintext, CancellationToken.None);
        var blob2 = await encryptor.EncryptAsync(plaintext, CancellationToken.None);

        Assert.NotEqual(blob1, blob2);
    }

    [Fact]
    public async Task Decrypt_rejects_tampered_ciphertext()
    {
        var encryptor = new KeyVaultEncryptor(FakeKv());
        var blob = await encryptor.EncryptAsync(RandomNumberGenerator.GetBytes(64), CancellationToken.None);
        blob[^1] ^= 0x01; // flip a bit in the ciphertext body

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => encryptor.DecryptAsync(blob, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Decrypt_rejects_unknown_envelope_version()
    {
        var encryptor = new KeyVaultEncryptor(FakeKv());
        var blob = await encryptor.EncryptAsync(RandomNumberGenerator.GetBytes(8), CancellationToken.None);
        blob[0] = 0xFE;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => encryptor.DecryptAsync(blob, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Decrypt_rejects_truncated_blob()
    {
        var encryptor = new KeyVaultEncryptor(FakeKv());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => encryptor.DecryptAsync(new byte[] { 1, 0 }, CancellationToken.None).AsTask());
    }
}
