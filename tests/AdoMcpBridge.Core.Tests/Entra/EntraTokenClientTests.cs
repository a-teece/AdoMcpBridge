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
            "openid",
            "profile",
            "offline_access",
            "https://mcp.dev.azure.com/Ado.Mcp.Tools",
        },
        AdoRestScopes = new List<string>
        {
            "openid",
            "profile",
            "offline_access",
            "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation",
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
    public async Task AcquireAdoRestTokenAsync_requests_the_ado_rest_scope_not_the_mcp_scope()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            token_type = "Bearer",
            expires_in = 3600,
            access_token = "ado-rest-access-token",
            refresh_token = "rotated-entra-refresh-token",
            id_token = wm.IssueIdToken(oid: "user-oid-789", upn: "carol@example.com"),
        });

        var sut = NewClient(opts);

        var result = await sut.AcquireAdoRestTokenAsync("stored-entra-refresh-token", CancellationToken.None);

        result.AccessToken.Should().Be("ado-rest-access-token");

        // The native ADO REST tools must obtain a token audienced for the classic
        // Azure DevOps REST resource (499b84ac.../user_impersonation), NOT the
        // Remote-MCP-server resource that EntraOptions.Scopes targets — the two
        // resources reject each other's tokens.
        var sentScope = wm.LastFormValue("scope");
        sentScope.Should().Be(string.Join(' ', opts.AdoRestScopes));
        sentScope.Should().NotBe(string.Join(' ', opts.Scopes));
        sentScope.Should().Contain("499b84ac-1321-427f-aa17-267ca6975798/user_impersonation");
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
    public async Task ExchangeAuthorizationCodeAsync_malformed_id_token_throws()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            token_type = "Bearer",
            expires_in = 3600,
            access_token = "ado-access-token-value",
            refresh_token = "entra-refresh-token-value",
            id_token = "notajwt",
        });

        var sut = NewClient(opts);

        var act = async () => await sut.ExchangeAuthorizationCodeAsync("c", "v", "https://localhost/cb", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.Unknown);
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_id_token_without_oid_throws()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        // Three-segment token whose payload base64url-decodes to "{}" (no oid claim).
        // "e30" is base64url("{}"); its length (3) also exercises the %4==3 padding path.
        wm.StubTokenEndpoint(200, new
        {
            token_type = "Bearer",
            expires_in = 3600,
            access_token = "ado-access-token-value",
            refresh_token = "entra-refresh-token-value",
            id_token = "aaa.e30.bbb",
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

    [Fact]
    public async Task Failure_without_error_property_yields_null_error_code()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(500, new { unrelated = "x" });

        var sut = NewClient(opts);

        var ex = await ((Func<Task>)(async () =>
            await sut.AcquireAdoTokenAsync("rt", CancellationToken.None))).Should().ThrowAsync<EntraAuthException>();
        ex.Which.StatusCode.Should().Be(500);
        ex.Which.EntraErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Null_access_token_throws()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            access_token = (string?)null,
            refresh_token = "rt",
            id_token = wm.IssueIdToken("oid", "u@example.com"),
        });

        var sut = NewClient(opts);

        var ex = await ((Func<Task>)(async () =>
            await sut.ExchangeAuthorizationCodeAsync("c", "v", "https://localhost/cb", CancellationToken.None)))
            .Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.Unknown);
    }

    [Fact]
    public async Task Null_id_token_throws()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            access_token = "at",
            refresh_token = "rt",
            expires_in = 3600,
            id_token = (string?)null,
        });

        var sut = NewClient(opts);

        var ex = await ((Func<Task>)(async () =>
            await sut.ExchangeAuthorizationCodeAsync("c", "v", "https://localhost/cb", CancellationToken.None)))
            .Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.Unknown);
    }

    [Fact]
    public async Task Missing_expires_in_defaults_and_authority_without_v2_suffix_works()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        // Authority without the "/v2.0" suffix exercises the alternate token-endpoint derivation.
        opts.Authority = $"{wm.Server.Url}/{wm.TenantId}";
        wm.StubTokenEndpoint(200, new
        {
            access_token = "at",
            refresh_token = "rt",
            id_token = wm.IssueIdToken("oid-x", "u@example.com"),
        });

        var sut = NewClient(opts);

        var result = await sut.ExchangeAuthorizationCodeAsync("c", "v", "https://localhost/cb", CancellationToken.None);

        result.UserObjectId.Should().Be("oid-x");
    }

    [Fact]
    public async Task Id_token_without_preferred_username_yields_empty_upn()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        var payload = Base64Url("{\"oid\":\"oid-only\"}"u8.ToArray());
        wm.StubTokenEndpoint(200, new
        {
            access_token = "at",
            refresh_token = "rt",
            expires_in = 3600,
            id_token = $"hdr.{payload}.sig",
        });

        var sut = NewClient(opts);

        var result = await sut.ExchangeAuthorizationCodeAsync("c", "v", "https://localhost/cb", CancellationToken.None);

        result.UserObjectId.Should().Be("oid-only");
        result.UserPrincipalName.Should().BeEmpty();
    }

    [Fact]
    public async Task Certificate_without_private_key_throws_CertificateUnavailable()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new { access_token = "at", refresh_token = "rt", id_token = wm.IssueIdToken("o", "u") });

        var certs = Substitute.For<ICertificateProvider>();
        certs.GetCertificateAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<X509Certificate2>(TestCertificates.CreatePublicOnly()));
        var sut = new EntraTokenClient(new HttpClient(), certs, Options.Create(opts),
            new FixedClock(DateTimeOffset.UtcNow), NullLogger<EntraTokenClient>.Instance);

        var ex = await ((Func<Task>)(async () =>
            await sut.AcquireAdoTokenAsync("rt", CancellationToken.None))).Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.CertificateUnavailable);
    }

    [Fact]
    public async Task Id_token_with_null_preferred_username_yields_empty_upn()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        var payload = Base64Url("{\"oid\":\"o2\",\"preferred_username\":null}"u8.ToArray());
        wm.StubTokenEndpoint(200, new
        {
            access_token = "at",
            refresh_token = "rt",
            expires_in = 3600,
            id_token = $"hdr.{payload}.sig",
        });

        var sut = NewClient(opts);

        var result = await sut.ExchangeAuthorizationCodeAsync("c", "v", "https://localhost/cb", CancellationToken.None);

        result.UserObjectId.Should().Be("o2");
        result.UserPrincipalName.Should().BeEmpty();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
