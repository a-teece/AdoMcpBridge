using System.Net;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Core.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.Proxy;

public sealed class ProxyIntegrationTests : IClassFixture<ProxyTestFixture>
{
    private readonly ProxyTestFixture _fx;
    public ProxyIntegrationTests(ProxyTestFixture fx) => _fx = fx;

    [Fact]
    public async Task Missing_bearer_returns_401_with_www_authenticate()
    {
        var client = _fx.CreateClient();
        var response = await client.GetAsync("/mcp/anything");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        challenge.Should().StartWith("Bearer");
        // RFC 9728: point clients at the bridge's own resource metadata.
        challenge.Should().Contain(
            "resource_metadata=\"https://localhost/.well-known/oauth-protected-resource/mcp/anything\"");
    }

    [Fact]
    public async Task Upstream_401_challenge_is_replaced_with_bridge_challenge()
    {
        _fx.Upstream.Reset();
        _fx.Upstream.Given(WireMock.RequestBuilders.Request.Create().WithPath("/secured").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(401)
                .WithHeader("WWW-Authenticate",
                    "Bearer resource_metadata=\"https://mcp.dev.azure.com/.well-known/oauth-protected-resource/Enate\""));

        var record = new TokenRecord(
            TokenHasher.Sha256Hex("ok-401"), "rh5", "cid",
            Convert.ToBase64String(new byte[] { 1 }),
            "oid", "u@example.com",
            _fx.Clock.UtcNow.AddMinutes(30), _fx.Clock.UtcNow.AddDays(14), _fx.Clock.UtcNow);
        _fx.TokenStore.FindByAccessTokenHashAsync(record.AccessTokenHash, Arg.Any<CancellationToken>())
            .Returns(record);
        _fx.Encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<byte[]>(System.Text.Encoding.UTF8.GetBytes("rt")));
        _fx.EntraClient.AcquireAdoTokenAsync("rt", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(
                new EntraTokenResult("ado-tok", "nrt", _fx.Clock.UtcNow.AddMinutes(50), "oid", "u@example.com")));

        var client = _fx.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/secured");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "ok-401");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        challenge.Should().NotContain("mcp.dev.azure.com");
        challenge.Should().Contain("ado-mcp-bridge");
        challenge.Should().Contain(
            "resource_metadata=\"https://localhost/.well-known/oauth-protected-resource/mcp/secured\"");
    }

    [Fact]
    public async Task Upstream_404_passes_through_without_foreign_challenge()
    {
        _fx.Upstream.Reset();
        _fx.Upstream.Given(WireMock.RequestBuilders.Request.Create().WithPath("/missing").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(404)
                .WithHeader("WWW-Authenticate", "Bearer realm=\"upstream\""));

        var record = new TokenRecord(
            TokenHasher.Sha256Hex("ok-404"), "rh6", "cid",
            Convert.ToBase64String(new byte[] { 1 }),
            "oid", "u@example.com",
            _fx.Clock.UtcNow.AddMinutes(30), _fx.Clock.UtcNow.AddDays(14), _fx.Clock.UtcNow);
        _fx.TokenStore.FindByAccessTokenHashAsync(record.AccessTokenHash, Arg.Any<CancellationToken>())
            .Returns(record);
        _fx.Encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<byte[]>(System.Text.Encoding.UTF8.GetBytes("rt")));
        _fx.EntraClient.AcquireAdoTokenAsync("rt", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(
                new EntraTokenResult("ado-tok", "nrt", _fx.Clock.UtcNow.AddMinutes(50), "oid", "u@example.com")));

        var client = _fx.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/missing");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "ok-404");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.WwwAuthenticate.Should().BeEmpty();
    }

    [Fact]
    public async Task Valid_bearer_swaps_authorization_and_forwards_to_upstream()
    {
        _fx.Upstream.Reset();
        _fx.Upstream.Given(WireMock.RequestBuilders.Request.Create().WithPath("/anything").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(200).WithBody("hi"));

        var record = new TokenRecord(
            AccessTokenHash: TokenHasher.Sha256Hex("good-token"),
            RefreshTokenHash: "rh",
            ClientId: "cid",
            EntraRefreshTokenEncrypted: Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            UserObjectId: "oid",
            UserPrincipalName: "u@example.com",
            AccessTokenExpiresAt: _fx.Clock.UtcNow.AddMinutes(30),
            RefreshTokenExpiresAt: _fx.Clock.UtcNow.AddDays(14),
            CreatedAt: _fx.Clock.UtcNow.AddMinutes(-1));

        _fx.TokenStore.FindByAccessTokenHashAsync(record.AccessTokenHash, Arg.Any<CancellationToken>())
            .Returns(record);
        _fx.Encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<byte[]>(System.Text.Encoding.UTF8.GetBytes("entra-refresh")));
        _fx.EntraClient.AcquireAdoTokenAsync("entra-refresh", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(
                new EntraTokenResult("ado-access-XYZ", "new-refresh",
                    _fx.Clock.UtcNow.AddMinutes(50), "oid", "u@example.com")));

        var client = _fx.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/anything");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "good-token");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = _fx.Upstream.LogEntries.ToList();
        logs.Should().ContainSingle();
        var seen = logs[0].RequestMessage.Headers!["Authorization"];
        seen.Should().ContainSingle().Which.Should().Be("Bearer ado-access-XYZ");
    }

    [Fact]
    public async Task Only_allowlisted_x_mcp_headers_reach_upstream()
    {
        _fx.Upstream.Reset();
        _fx.Upstream.Given(WireMock.RequestBuilders.Request.Create().WithPath("/anything").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(200));

        var record = new TokenRecord(
            TokenHasher.Sha256Hex("good2"), "rh2", "cid",
            Convert.ToBase64String(new byte[] { 9 }),
            "oid", "u@example.com",
            _fx.Clock.UtcNow.AddMinutes(30), _fx.Clock.UtcNow.AddDays(14), _fx.Clock.UtcNow);

        _fx.TokenStore.FindByAccessTokenHashAsync(record.AccessTokenHash, Arg.Any<CancellationToken>())
            .Returns(record);
        _fx.Encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<byte[]>(System.Text.Encoding.UTF8.GetBytes("rt")));
        _fx.EntraClient.AcquireAdoTokenAsync("rt", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(
                new EntraTokenResult("ado-tok", "nrt", _fx.Clock.UtcNow.AddMinutes(50), "oid", "u@example.com")));

        var client = _fx.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/anything");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "good2");
        req.Headers.Add("X-MCP-Toolsets", "repos");
        req.Headers.Add("X-MCP-Readonly", "true");
        req.Headers.Add("X-Forwarded-Host", "evil.example.com");
        req.Headers.Add("X-Custom-Sneaky", "leak");

        var response = await client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var seen = _fx.Upstream.LogEntries.ToList()[0].RequestMessage.Headers!;
        seen.Should().ContainKey("X-MCP-Toolsets");
        seen.Should().ContainKey("X-MCP-Readonly");
        seen.Should().NotContainKey("X-Forwarded-Host");
        seen.Should().NotContainKey("X-Custom-Sneaky");
    }

    [Fact]
    public async Task Expired_bearer_returns_401()
    {
        var record = new TokenRecord(
            TokenHasher.Sha256Hex("expired"), "rh3", "cid",
            Convert.ToBase64String(new byte[] { 1 }),
            "oid", "u@example.com",
            _fx.Clock.UtcNow.AddMinutes(-1), _fx.Clock.UtcNow.AddDays(14), _fx.Clock.UtcNow.AddHours(-2));

        _fx.TokenStore.FindByAccessTokenHashAsync(record.AccessTokenHash, Arg.Any<CancellationToken>())
            .Returns(record);

        var client = _fx.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/anything");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "expired");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("invalid_token");
    }

    [Fact]
    public async Task Upstream_500_is_mapped_to_502_translated_body()
    {
        _fx.Upstream.Reset();
        _fx.Upstream.Given(WireMock.RequestBuilders.Request.Create().WithPath("/boom").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(500).WithBody("raw upstream"));

        var record = new TokenRecord(
            TokenHasher.Sha256Hex("ok"), "rh4", "cid",
            Convert.ToBase64String(new byte[] { 1 }),
            "oid", "u@example.com",
            _fx.Clock.UtcNow.AddMinutes(30), _fx.Clock.UtcNow.AddDays(14), _fx.Clock.UtcNow);

        _fx.TokenStore.FindByAccessTokenHashAsync(record.AccessTokenHash, Arg.Any<CancellationToken>())
            .Returns(record);
        _fx.Encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<byte[]>(System.Text.Encoding.UTF8.GetBytes("rt")));
        _fx.EntraClient.AcquireAdoTokenAsync("rt", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(
                new EntraTokenResult("ado-tok", "nrt", _fx.Clock.UtcNow.AddMinutes(50), "oid", "u@example.com")));

        var client = _fx.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/mcp/boom");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "ok");
        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("raw upstream");
        body.Should().Contain("upstream_unavailable");
    }
}
