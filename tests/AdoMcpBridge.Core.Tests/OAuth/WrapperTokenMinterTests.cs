using AdoMcpBridge.Core.OAuth;
using FluentAssertions;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.OAuth;

public sealed class WrapperTokenMinterTests
{
    private readonly IClock _clock = Substitute.For<IClock>();

    public WrapperTokenMinterTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Mint_produces_distinct_access_and_refresh_tokens()
    {
        var minter = new WrapperTokenMinter(_clock);
        var pair = minter.MintPair();
        pair.AccessToken.Should().NotBe(pair.RefreshToken);
        pair.AccessToken.Length.Should().BeGreaterThan(40);
        pair.RefreshToken.Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public void Mint_returns_decoded_32_byte_tokens()
    {
        var minter = new WrapperTokenMinter(_clock);
        var pair = minter.MintPair();
        DecodeBase64Url(pair.AccessToken).Length.Should().Be(32);
        DecodeBase64Url(pair.RefreshToken).Length.Should().Be(32);
    }

    [Fact]
    public void Hash_is_sha256_hex_lowercase_64_chars()
    {
        var minter = new WrapperTokenMinter(_clock);
        var hash = minter.Hash("token-value");
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Lifetimes_match_spec()
    {
        var minter = new WrapperTokenMinter(_clock);
        minter.AccessTokenExpiresAt.Should().Be(_clock.UtcNow.AddHours(1));
        minter.RefreshTokenExpiresAt.Should().Be(_clock.UtcNow.AddDays(14));
        minter.AuthorizationCodeExpiresAt.Should().Be(_clock.UtcNow.AddSeconds(60));
    }

    [Fact]
    public void MintOpaque_produces_unique_values()
    {
        var minter = new WrapperTokenMinter(_clock);
        var a = minter.MintOpaque();
        var b = minter.MintOpaque();
        a.Should().NotBe(b);
    }

    private static byte[] DecodeBase64Url(string s)
    {
        var pad = s.Replace('-', '+').Replace('_', '/');
        switch (pad.Length % 4) { case 2: pad += "=="; break; case 3: pad += "="; break; }
        return Convert.FromBase64String(pad);
    }
}
