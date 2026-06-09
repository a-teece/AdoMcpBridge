using System.Net;
using AdoMcpBridge.Core.Abstractions;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests;

public sealed class AuthorizeGetTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _f;
    public AuthorizeGetTests(BridgeApiFactory f) => _f = f;

    [Fact]
    public async Task Get_authorize_returns_consent_html_for_valid_request()
    {
        await _f.Store.AddClientAsync(
            new RegisteredClient("cid", "Claude", new[] { "https://cb/x" }, DateTimeOffset.UtcNow), default);
        var client = _f.CreateClient();

        var url = "/authorize?response_type=code&client_id=cid&redirect_uri=https%3A%2F%2Fcb%2Fx" +
                  "&code_challenge=abc123abc123abc123abc123abc123abc123abc123abc" +
                  "&code_challenge_method=S256&state=xyz&scope=mcp";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Claude");
        body.Should().Contain("Approve");
        body.Should().Contain("name=\"session_id\"");
    }

    [Fact]
    public async Task Get_authorize_returns_bad_request_for_unknown_client()
    {
        var client = _f.CreateClient();
        var url = "/authorize?response_type=code&client_id=nope&redirect_uri=https%3A%2F%2Fcb%2Fx" +
                  "&code_challenge=abc123abc123abc123abc123abc123abc123abc123abc" +
                  "&code_challenge_method=S256&state=xyz";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
    }
}
