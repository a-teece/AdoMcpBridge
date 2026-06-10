using System.Net;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests;

public sealed class EntraCallbackTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _f;
    public EntraCallbackTests(BridgeApiFactory f) => _f = f;

    [Fact]
    public async Task Successful_callback_redirects_to_client_with_code_and_state()
    {
        await _f.Store.AddClientAsync(
            new RegisteredClient("cid", "Claude", new[] { "https://cb/x" }, DateTimeOffset.UtcNow), default);
        var cache = _f.Services.GetRequiredService<IAuthorizationSessionCache>();
        await cache.PutAsync(new AuthorizationSession(
            "s1", "cid", "https://cb/x", "client-challenge", "S256",
            "client-state-AAA", "verifier-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "entra-state-EEE", DateTimeOffset.UtcNow.AddMinutes(5)), default);

        _f.EntraClient.ExchangeAuthorizationCodeAsync(
            "entra-code", "verifier-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(new EntraTokenResult(
                "ado-at", "entra-rt", DateTimeOffset.UtcNow.AddHours(1),
                "user-oid", "alice@example")));

        // Entra only ever sends code + state to the redirect URI; the
        // session must be correlated via state, never extra parameters.
        var http = _f.CreateClient(new() { AllowAutoRedirect = false });
        var url = "/authorize/callback?code=entra-code&state=entra-state-EEE";
        var resp = await http.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location!.ToString().Should().StartWith("https://cb/x?code=");
        resp.Headers.Location.Query.Should().Contain("state=client-state-AAA");
    }

    [Fact]
    public async Task Callback_with_unknown_state_returns_400()
    {
        var cache = _f.Services.GetRequiredService<IAuthorizationSessionCache>();
        await cache.PutAsync(new AuthorizationSession(
            "s2", "cid", "https://cb/x", "ch", "S256", "cs", "ev", "expected-state",
            DateTimeOffset.UtcNow.AddMinutes(5)), default);

        var http = _f.CreateClient(new() { AllowAutoRedirect = false });
        var resp = await http.GetAsync("/authorize/callback?code=c&state=WRONG");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("invalid_request");
    }

    [Fact]
    public async Task Callback_with_expired_session_returns_400()
    {
        var cache = _f.Services.GetRequiredService<IAuthorizationSessionCache>();
        await cache.PutAsync(new AuthorizationSession(
            "s3", "cid", "https://cb/x", "ch", "S256", "cs", "ev", "expired-state",
            _f.Clock.UtcNow.AddMinutes(-1)), default);

        var http = _f.CreateClient(new() { AllowAutoRedirect = false });
        var resp = await http.GetAsync("/authorize/callback?code=c&state=expired-state");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("invalid_request");
    }
}
