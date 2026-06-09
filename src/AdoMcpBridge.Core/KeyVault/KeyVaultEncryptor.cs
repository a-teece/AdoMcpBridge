using AdoMcpBridge.Core.Abstractions;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace AdoMcpBridge.Core.KeyVault;

public sealed class KeyVaultEncryptor : IKeyVaultEncryptor
{
    private readonly CryptographyClient _client;

    public KeyVaultEncryptor(CryptographyClient client) => _client = client;

    public async ValueTask<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct)
    {
        var result = await _client.EncryptAsync(EncryptionAlgorithm.RsaOaep256, plaintext, ct);
        return result.Ciphertext;
    }

    public async ValueTask<byte[]> DecryptAsync(byte[] ciphertext, CancellationToken ct)
    {
        var result = await _client.DecryptAsync(EncryptionAlgorithm.RsaOaep256, ciphertext, ct);
        return result.Plaintext;
    }
}
