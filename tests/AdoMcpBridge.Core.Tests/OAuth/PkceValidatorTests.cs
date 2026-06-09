using System.Security.Cryptography;
using System.Text;
using AdoMcpBridge.Core.OAuth;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.OAuth;

public sealed class PkceValidatorTests
{
    private static string S256(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void Verify_returns_true_for_matching_S256_pair()
    {
        var verifier = "abc123abc123abc123abc123abc123abc123abc123abc";
        var challenge = S256(verifier);
        new PkceValidator().Verify(verifier, challenge, "S256").Should().BeTrue();
    }

    [Fact]
    public void Verify_returns_false_for_wrong_verifier()
    {
        var challenge = S256("rightverifierrightverifierrightverifierrightver");
        new PkceValidator().Verify("wrongverifierwrongverifierwrongverifierwrongver", challenge, "S256")
            .Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_plain_method()
    {
        new PkceValidator().Verify("v", "v", "plain").Should().BeFalse();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    public void Verify_rejects_verifier_below_43_chars(string verifier)
    {
        new PkceValidator().Verify(verifier, "ignored", "S256").Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_verifier_above_128_chars()
    {
        var v = new string('a', 129);
        new PkceValidator().Verify(v, S256(v), "S256").Should().BeFalse();
    }
}
