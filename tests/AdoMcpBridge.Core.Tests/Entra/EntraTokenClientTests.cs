using System.Security.Cryptography.X509Certificates;
using AdoMcpBridge.Core.Entra;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class EntraTokenClientTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }

    private static EntraOptions OptionsFor(WireMockEntra wm) => new()
    {
        TenantId = wm.TenantId,
        ClientId = "cid",
        CertificateName = "ado-mcp-bridge",
        KeyVaultUri = "https://kv.example/",
        Authority = wm.Authority,
        Scopes = new List<string>
        {
            "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation",
            "offline_access",
        },
    };

    private static ICertificateProvider CertProvider()
    {
        var provider = Substitute.For<ICertificateProvider>();
        provider.GetCertificateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<X509Certificate2>(TestCertificates.CreateSelfSigned()));
        return provider;
    }

    private static EntraTokenClient NewClient(EntraOptions opts) => new(
        new HttpClient(),
        CertProvider(),
        Options.Create(opts),
        new FixedClock(DateTimeOffset.UtcNow),
        NullLogger<EntraTokenClient>.Instance);

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_returns_tokens_and_user_identity()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            token_type = "Bearer",
            scope = string.Join(' ', opts.Scopes),
            expires_in = 3600,
            ext_expires_in = 3600,
            access_token = "ado-access-token-value",
            refresh_token = "entra-refresh-token-value",
            id_token = wm.IssueIdToken(oid: "user-oid-123", upn: "alice@example.com"),
        });

        var sut = NewClient(opts);

        var result = await sut.ExchangeAuthorizationCodeAsync(
            code: "auth-code-abc",
            codeVerifier: "pkce-verifier-xyz",
            redirectUri: "https://localhost:5001/callback",
            ct: CancellationToken.None);

        result.AccessToken.Should().Be("ado-access-token-value");
        result.RefreshToken.Should().Be("entra-refresh-token-value");
        result.UserObjectId.Should().Be("user-oid-123");
        result.UserPrincipalName.Should().Be("alice@example.com");
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task AcquireAdoTokenAsync_swaps_refresh_token_for_ado_access_token()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            token_type = "Bearer",
            scope = string.Join(' ', opts.Scopes),
            expires_in = 3600,
            ext_expires_in = 3600,
            access_token = "fresh-ado-access-token",
            refresh_token = "rotated-entra-refresh-token",
            id_token = wm.IssueIdToken(oid: "user-oid-456", upn: "bob@example.com"),
        });

        var sut = NewClient(opts);

        var result = await sut.AcquireAdoTokenAsync("stored-entra-refresh-token", CancellationToken.None);

        result.AccessToken.Should().Be("fresh-ado-access-token");
        result.RefreshToken.Should().Be("rotated-entra-refresh-token");
        result.UserObjectId.Should().Be("user-oid-456");
    }

    [Fact]
    [Trait("category", "security")]
    public async Task AcquireAdoTokenAsync_throws_EntraAuthException_on_401_invalid_grant()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(401, new
        {
            error = "invalid_grant",
            error_description = "AADSTS70008: The refresh token has expired.",
        });

        var sut = NewClient(opts);

        var act = async () => await sut.AcquireAdoTokenAsync("expired-refresh-token", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.RefreshRejected);
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.EntraErrorCode.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_without_refresh_token_throws()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            token_type = "Bearer",
            expires_in = 3600,
            access_token = "ado-access-token-value",
            id_token = wm.IssueIdToken(oid: "oid", upn: "u@example.com"),
        });

        var sut = NewClient(opts);

        var act = async () => await sut.ExchangeAuthorizationCodeAsync("c", "v", "https://localhost/cb", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.Unknown);
    }

    [Fact]
    public async Task AcquireAdoTokenAsync_maps_transport_failure_to_Transport_failure()
    {
        var opts = new EntraOptions
        {
            TenantId = "tid",
            ClientId = "cid",
            CertificateName = "ado-mcp-bridge",
            KeyVaultUri = "https://kv.example/",
            Authority = "http://127.0.0.1:1/tid/v2.0",
            Scopes = new List<string> { "scope", "offline_access" },
        };

        var sut = NewClient(opts);

        var act = async () => await sut.AcquireAdoTokenAsync("rt", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.Transport);
    }
}
