using System.Net;
using System.Net.Http.Json;
using System.Text;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Api.Tests;

public sealed class TokenRefreshGrantTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _f;
    public TokenRefreshGrantTests(BridgeApiFactory f) => _f = f;

    [Fact]
    public async Task Refresh_rotates_tokens_and_invalidates_old_refresh()
    {
        var minter = _f.Services.GetRequiredService<WrapperTokenMinter>();
        const string oldRefresh = "REFRESH-ABC";
        var record = new TokenRecord(
            AccessTokenHash: minter.Hash("ANY"),
            RefreshTokenHash: minter.Hash(oldRefresh),
            ClientId: "cid",
            EntraRefreshTokenEncrypted: Convert.ToBase64String(Encoding.UTF8.GetBytes("ent-rt")),
            UserObjectId: "oid",
            UserPrincipalName: "alice",
            AccessTokenExpiresAt: _f.Clock.UtcNow.AddHours(1),
            RefreshTokenExpiresAt: _f.Clock.UtcNow.AddDays(14),
            CreatedAt: _f.Clock.UtcNow);
        await _f.Store.AddTokenAsync(record, default);

        var http = _f.CreateClient();
        var resp = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", oldRefresh),
            new KeyValuePair<string, string>("client_id", "cid"),
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["access_token"].ToString().Should().NotBeNullOrWhiteSpace();
        body["refresh_token"].ToString().Should().NotBe(oldRefresh);

        var resp2 = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", oldRefresh),
            new KeyValuePair<string, string>("client_id", "cid"),
        }));
        resp2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_with_unknown_token_returns_invalid_grant()
    {
        var http = _f.CreateClient();
        var resp = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", "nope"),
            new KeyValuePair<string, string>("client_id", "cid"),
        }));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Refresh_with_expired_token_returns_invalid_grant()
    {
        var minter = _f.Services.GetRequiredService<WrapperTokenMinter>();
        const string r = "REFRESH-EXP";
        await _f.Store.AddTokenAsync(new TokenRecord(
            minter.Hash("A"), minter.Hash(r), "cid",
            Convert.ToBase64String(new byte[] { 1 }), "oid", "alice",
            _f.Clock.UtcNow.AddHours(-1), _f.Clock.UtcNow.AddHours(-1),
            _f.Clock.UtcNow.AddDays(-15)), default);

        var http = _f.CreateClient();
        var resp = await http.PostAsync("/token", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", r),
            new KeyValuePair<string, string>("client_id", "cid"),
        }));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
