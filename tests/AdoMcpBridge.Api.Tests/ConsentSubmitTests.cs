using System.Net;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Api.Tests;

public sealed class ConsentSubmitTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _f;
    public ConsentSubmitTests(BridgeApiFactory f) => _f = f;

    [Fact]
    public async Task Approve_redirects_to_entra_authorize_with_state_and_PKCE()
    {
        await _f.Store.AddClientAsync(
            new RegisteredClient("cid", "Claude", new[] { "https://cb/x" }, DateTimeOffset.UtcNow), default);
        var cache = _f.Services.GetRequiredService<IAuthorizationSessionCache>();
        var session = new AuthorizationSession(
            "sess1", "cid", "https://cb/x", "chal", "S256", "client-state",
            "verifier-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "entra-state",
            DateTimeOffset.UtcNow.AddMinutes(10));
        await cache.PutAsync(session, default);

        var client = _f.CreateClient(new() { AllowAutoRedirect = false });
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("session_id", "sess1"),
            new KeyValuePair<string, string>("decision", "approve"),
        });
        var resp = await client.PostAsync("/authorize/consent", content);

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location!.ToString().Should().StartWith("https://login.microsoftonline.com/");
        resp.Headers.Location.Query.Should().Contain("state=entra-state");
        resp.Headers.Location.Query.Should().Contain("code_challenge_method=S256");
    }

    [Fact]
    public async Task Deny_redirects_back_to_client_with_access_denied()
    {
        var cache = _f.Services.GetRequiredService<IAuthorizationSessionCache>();
        await cache.PutAsync(new AuthorizationSession(
            "sess2", "cid", "https://cb/x", "ch", "S256", "client-state",
            "ev", "es", DateTimeOffset.UtcNow.AddMinutes(5)), default);

        var client = _f.CreateClient(new() { AllowAutoRedirect = false });
        var resp = await client.PostAsync("/authorize/consent",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("session_id", "sess2"),
                new KeyValuePair<string, string>("decision", "deny"),
            }));
        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location!.ToString().Should().StartWith("https://cb/x");
        resp.Headers.Location.Query.Should().Contain("error=access_denied");
        resp.Headers.Location.Query.Should().Contain("state=client-state");
    }
}
