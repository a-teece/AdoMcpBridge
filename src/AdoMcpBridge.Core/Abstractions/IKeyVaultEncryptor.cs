namespace AdoMcpBridge.Core.Abstractions;

public interface IKeyVaultEncryptor
{
    ValueTask<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct);
    ValueTask<byte[]> DecryptAsync(byte[] ciphertext, CancellationToken ct);
}
