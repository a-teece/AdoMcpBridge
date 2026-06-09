using AdoMcpBridge.Api.Proxy;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.Proxy;

public sealed class TokenHasherTests
{
    [Fact]
    public void Sha256Hex_KnownVector_ReturnsLowercaseHex()
    {
        // SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
        TokenHasher.Sha256Hex("abc")
            .Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Sha256Hex_EmptyString_ReturnsEmptyStringHash()
    {
        TokenHasher.Sha256Hex(string.Empty)
            .Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void Sha256Hex_Null_Throws()
    {
        var act = () => TokenHasher.Sha256Hex(null!);
        act.Should().Throw<System.ArgumentNullException>();
    }
}
