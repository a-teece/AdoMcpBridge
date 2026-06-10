using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AdoMcpBridge.Core.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests;

public sealed class OAuthEndToEndTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _f;
    public OAuthEndToEndTests(BridgeApiFactory f) => _f = f;

    private static string S256(string v)
    {
        Span<byte> h = stackalloc byte[32];
        SHA256.HashData(Encoding.ASCII.GetBytes(v), h);
        return Convert.ToBase64String(h).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public async Task Full_flow_register_authorize_token_refresh_revoke()
    {
        var http = _f.CreateClient(new() { AllowAutoRedirect = false });

        // 1. Register
        var reg = await http.PostAsJsonAsync("/register", new
        {
            client_name = "Claude E2E",
            redirect_uris = new[] { "https://claude.test/cb" },
        });
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var regBody = await reg.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var clientId = regBody!["client_id"].ToString()!;

        // 2. GET /authorize -> consent HTML
        const string verifier = "verifier-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var challenge = S256(verifier);
        var auth = await http.GetAsync(
            $"/authorize?response_type=code&client_id={clientId}&redirect_uri=https%3A%2F%2Fclaude.test%2Fcb" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=client-state");
        auth.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await auth.Content.ReadAsStringAsync();
        var sessionId = ExtractValue(html, "name=\"session_id\" value=\"", "\"");

        // 3. POST consent (approve) -> redirect to Entra
        var entraRedirect = await http.PostAsync("/authorize/consent", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("session_id", sessionId),
            new KeyValuePair<string, string>("decision", "approve"),
        }));
        entraRedirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var entraState = System.Web.HttpUtility.ParseQueryString(entraRedirect.Headers.Location!.Query)["state"];
        entraState.Should().NotBeNullOrEmpty();

        // 4. Simulate Entra callback. Mock IEntraTokenClient.
        _f.EntraClient.ExchangeAuthorizationCodeAsync(
            "entra-code", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(new EntraTokenResult(
                "ado-at", "ent-rt", _f.Clock.UtcNow.AddHours(1), "oid", "alice@test")));

        // Only code + state — Entra sends nothing else to the redirect URI.
        var cb = await http.GetAsync(
            $"/authorize/callback?code=entra-code&state={Uri.EscapeDataString(entraState!)}");
        cb.StatusCode.Should().Be(HttpStatusCode.Redirect);
        cb.Headers.Location!.ToString().Should().StartWith("https://claude.test/cb?code=");
        var authCode = System.Web.HttpUtility.ParseQueryString(cb.Headers.Location.Query)["code"]!;

        // 5. /token authorization_code
        var tok = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", authCode),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("redirect_uri", "https://claude.test/cb"),
            new KeyValuePair<string, string>("code_verifier", verifier),
        }));
        tok.StatusCode.Should().Be(HttpStatusCode.OK);
        var pair1 = await tok.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var refresh = pair1!["refresh_token"].ToString()!;

        // 6. Refresh
        var tok2 = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refresh),
            new KeyValuePair<string, string>("client_id", clientId),
        }));
        tok2.StatusCode.Should().Be(HttpStatusCode.OK);
        var pair2 = await tok2.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var refresh2 = pair2!["refresh_token"].ToString()!;
        refresh2.Should().NotBe(refresh);

        // 7. Revoke
        var rev = await http.PostAsync("/revoke", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("token", refresh2),
        }));
        rev.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string ExtractValue(string html, string before, string after)
    {
        var i = html.IndexOf(before, StringComparison.Ordinal);
        if (i < 0) throw new InvalidOperationException("marker not found");
        var start = i + before.Length;
        var end = html.IndexOf(after, start, StringComparison.Ordinal);
        return html.Substring(start, end - start);
    }
}
