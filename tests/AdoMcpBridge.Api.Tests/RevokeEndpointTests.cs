using System.Net;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Api.Tests;

public sealed class RevokeEndpointTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _f;
    public RevokeEndpointTests(BridgeApiFactory f) => _f = f;

    [Fact]
    public async Task Revoke_returns_200_and_removes_token()
    {
        var minter = _f.Services.GetRequiredService<WrapperTokenMinter>();
        const string r = "REVOKE-ME";
        await _f.Store.AddTokenAsync(new TokenRecord(
            minter.Hash("A"), minter.Hash(r), "cid",
            Convert.ToBase64String(new byte[] { 1 }), "oid", "alice",
            _f.Clock.UtcNow.AddHours(1), _f.Clock.UtcNow.AddDays(14), _f.Clock.UtcNow), default);

        var http = _f.CreateClient();
        var resp = await http.PostAsync("/revoke", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("token", r),
            new KeyValuePair<string, string>("token_type_hint", "refresh_token"),
        }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _f.Store.FindByRefreshTokenHashAsync(minter.Hash(r), default)).Should().BeNull();
    }

    [Fact]
    public async Task Revoke_returns_200_even_when_token_unknown()
    {
        var http = _f.CreateClient();
        var resp = await http.PostAsync("/revoke", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("token", "nope"),
        }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
