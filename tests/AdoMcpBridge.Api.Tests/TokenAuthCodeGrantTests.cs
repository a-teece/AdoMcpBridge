using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AdoMcpBridge.Core.Abstractions;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests;

public sealed class TokenAuthCodeGrantTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _f;
    public TokenAuthCodeGrantTests(BridgeApiFactory f) => _f = f;

    private static string S256(string v)
    {
        Span<byte> h = stackalloc byte[32];
        SHA256.HashData(Encoding.ASCII.GetBytes(v), h);
        return Convert.ToBase64String(h).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public async Task Auth_code_grant_returns_access_and_refresh_token_and_consumes_code()
    {
        const string verifier = "verifier-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var challenge = S256(verifier);
        await _f.Store.AddClientAsync(
            new RegisteredClient("cid", "Claude", new[] { "https://cb/x" }, DateTimeOffset.UtcNow), default);
        await _f.Store.AddAuthorizationCodeAsync(new AuthorizationCodeRecord(
            "AUTH-CODE-1", "cid", "https://cb/x", challenge, "S256",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("entra-rt")),
            "oid", "alice", _f.Clock.UtcNow.AddSeconds(60)), default);

        var http = _f.CreateClient();
        var resp = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", "AUTH-CODE-1"),
            new KeyValuePair<string, string>("client_id", "cid"),
            new KeyValuePair<string, string>("redirect_uri", "https://cb/x"),
            new KeyValuePair<string, string>("code_verifier", verifier),
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["token_type"].ToString().Should().Be("Bearer");
        body["expires_in"].ToString().Should().Be("3600");
        body.Should().ContainKey("access_token");
        body.Should().ContainKey("refresh_token");

        var resp2 = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", "AUTH-CODE-1"),
            new KeyValuePair<string, string>("client_id", "cid"),
            new KeyValuePair<string, string>("redirect_uri", "https://cb/x"),
            new KeyValuePair<string, string>("code_verifier", verifier),
        }));
        resp2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Auth_code_grant_rejects_bad_pkce_verifier()
    {
        var challenge = S256("right-verifier-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        await _f.Store.AddClientAsync(
            new RegisteredClient("cid2", "Claude", new[] { "https://cb/x" }, DateTimeOffset.UtcNow), default);
        await _f.Store.AddAuthorizationCodeAsync(new AuthorizationCodeRecord(
            "AUTH-CODE-2", "cid2", "https://cb/x", challenge, "S256",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("entra-rt")),
            "oid", "alice", _f.Clock.UtcNow.AddSeconds(60)), default);

        var http = _f.CreateClient();
        var resp = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", "AUTH-CODE-2"),
            new KeyValuePair<string, string>("client_id", "cid2"),
            new KeyValuePair<string, string>("redirect_uri", "https://cb/x"),
            new KeyValuePair<string, string>("code_verifier", "wrong-verifier-aaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
        }));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("invalid_grant");
    }
}
