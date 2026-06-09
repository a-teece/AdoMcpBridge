using AdoMcpBridge.Core.KeyVault;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.KeyVault;

public sealed class KeyVaultEncryptorTests
{
    [Fact]
    public async Task EncryptAsync_uses_RsaOaep256_and_returns_ciphertext()
    {
        var crypto = Substitute.For<CryptographyClient>();
        var plaintext = new byte[] { 1, 2, 3 };
        var ciphertext = new byte[] { 9, 9, 9 };

        crypto.EncryptAsync(
                EncryptionAlgorithm.RsaOaep256,
                Arg.Is<byte[]>(b => b.SequenceEqual(plaintext)),
                Arg.Any<CancellationToken>())
            .Returns(CryptographyModelFactory.EncryptResult(
                keyId: "kid", ciphertext: ciphertext, algorithm: EncryptionAlgorithm.RsaOaep256));

        var encryptor = new KeyVaultEncryptor(crypto);
        var result = await encryptor.EncryptAsync(plaintext, CancellationToken.None);

        Assert.Equal(ciphertext, result);
    }

    [Fact]
    public async Task DecryptAsync_uses_RsaOaep256_and_returns_plaintext()
    {
        var crypto = Substitute.For<CryptographyClient>();
        var ciphertext = new byte[] { 9, 9, 9 };
        var plaintext = new byte[] { 1, 2, 3 };

        crypto.DecryptAsync(
                EncryptionAlgorithm.RsaOaep256,
                Arg.Is<byte[]>(b => b.SequenceEqual(ciphertext)),
                Arg.Any<CancellationToken>())
            .Returns(CryptographyModelFactory.DecryptResult(
                keyId: "kid", plaintext: plaintext, algorithm: EncryptionAlgorithm.RsaOaep256));

        var encryptor = new KeyVaultEncryptor(crypto);
        var result = await encryptor.DecryptAsync(ciphertext, CancellationToken.None);

        Assert.Equal(plaintext, result);
    }
}
