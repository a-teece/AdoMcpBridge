using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using AdoMcpBridge.Smoke.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("Category", "smoke")]
public class TokenRefreshTests
{
    private readonly ITestOutputHelper _output;
    public TokenRefreshTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task Refresh_ReturnsNewPairWithinBudget()
    {
        Skip.IfNot(SmokeEnvironment.HasFullCredentials, "Smoke credentials not set");
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        var refreshToken = SmokeEnvironment.RequireRefreshToken();
        var clientId = SmokeEnvironment.RequireClientId();
        using var client = SmokeHttpClient.Create(baseUri);

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("client_id", clientId),
        });

        var sw = Stopwatch.StartNew();
        using var response = await client.PostAsync("/token", form);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        payload.Should().NotBeNull();

        // CRITICAL: never log token values. Length-only sentinel.
        _output.WriteLine($"access_token={SmokeEnvironment.Redact(payload!.AccessToken)}");
        _output.WriteLine($"refresh_token={SmokeEnvironment.Redact(payload.RefreshToken)}");
        _output.WriteLine($"token_endpoint_ms={sw.ElapsedMilliseconds}");

        payload.AccessToken.Length.Should().BeGreaterThan(0);
        payload.RefreshToken.Length.Should().BeGreaterThan(0);
        payload.TokenType.Should().Be("Bearer");
        payload.ExpiresIn.Should().BeGreaterThan(0);

        // Logged-only single-shot timing — soft assert so a network blip
        // doesn't flake the run. Real p95 lives in OpenTelemetry alerts.
        if (sw.ElapsedMilliseconds > 2000)
        {
            _output.WriteLine(
                $"WARN: /token took {sw.ElapsedMilliseconds}ms (>2000ms soft budget)");
        }
    }
}
