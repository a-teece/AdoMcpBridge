using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests;

public sealed class RegisterEndpointTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _factory;
    public RegisterEndpointTests(BridgeApiFactory f) => _factory = f;

    [Fact]
    public async Task Post_register_creates_public_client_and_persists_it()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            client_name = "Claude Code",
            redirect_uris = new[] { "https://claude.ai/api/mcp/auth_callback" },
        };

        var resp = await client.PostAsJsonAsync("/register", req);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["client_id"].ToString().Should().NotBeNullOrWhiteSpace();
        body.Should().NotContainKey("client_secret");
        body["token_endpoint_auth_method"].ToString().Should().Be("none");

        var stored = await _factory.Store.FindClientAsync(body["client_id"].ToString()!, default);
        stored.Should().NotBeNull();
        stored!.RedirectUris.Should().ContainSingle().Which.Should().Be("https://claude.ai/api/mcp/auth_callback");
    }

    [Fact]
    public async Task Post_register_rejects_missing_redirect_uris()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/register", new { client_name = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["error"].Should().Be("invalid_request");
    }

    [Fact]
    public async Task Post_register_rejects_non_https_redirect()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/register",
            new { client_name = "X", redirect_uris = new[] { "http://evil.example/cb" } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // RFC 8252 §7.3: native apps (Claude Code among them) redirect to a
    // loopback address over plain http; the AS must accept it.
    [Theory]
    [InlineData("http://127.0.0.1:51234/callback")]
    [InlineData("http://localhost:51234/callback")]
    [InlineData("http://[::1]:51234/callback")]
    public async Task Post_register_accepts_http_loopback_redirect(string uri)
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/register",
            new { client_name = "Claude Code", redirect_uris = new[] { uri } });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
