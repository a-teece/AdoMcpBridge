namespace AdoMcpBridge.Core.Tests;

public sealed class InMemoryTokenStoreTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task AddClient_then_FindClient_returns_client()
    {
        var store = new InMemoryTokenStore();
        var client = new RegisteredClient("cid", "name", new[] { "https://x/cb" }, DateTimeOffset.UtcNow);
        await store.AddClientAsync(client, Ct);
        var found = await store.FindClientAsync("cid", Ct);
        Assert.Equal(client, found);
    }

    [Fact]
    public async Task FindClient_unknown_id_returns_null()
    {
        var store = new InMemoryTokenStore();
        Assert.Null(await store.FindClientAsync("missing", Ct));
    }

    [Fact]
    public async Task AddClient_duplicate_id_throws_InvalidOperationException()
    {
        var store = new InMemoryTokenStore();
        var c = new RegisteredClient("cid", "n", Array.Empty<string>(), DateTimeOffset.UtcNow);
        await store.AddClientAsync(c, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddClientAsync(c, Ct).AsTask());
    }

    [Fact]
    public async Task ConsumeAuthorizationCode_returns_then_removes_record()
    {
        var store = new InMemoryTokenStore();
        var rec = new AuthorizationCodeRecord(
            "code", "cid", "https://x/cb", "ch", "S256", "ZW5j", "oid", "upn@x",
            DateTimeOffset.UtcNow.AddSeconds(60));
        await store.AddAuthorizationCodeAsync(rec, Ct);

        var first = await store.ConsumeAuthorizationCodeAsync("code", Ct);
        var second = await store.ConsumeAuthorizationCodeAsync("code", Ct);

        Assert.Equal(rec, first);
        Assert.Null(second);
    }

    [Fact]
    public async Task ConsumeAuthorizationCode_unknown_returns_null()
    {
        var store = new InMemoryTokenStore();
        Assert.Null(await store.ConsumeAuthorizationCodeAsync("missing", Ct));
    }

    private static TokenRecord NewToken(string accessHash, string refreshHash) => new(
        accessHash, refreshHash, "cid", "ZW5j", "oid", "upn@x",
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddDays(14),
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task AddToken_then_FindByAccessTokenHash_returns_it()
    {
        var store = new InMemoryTokenStore();
        var t = NewToken("ah1", "rh1");
        await store.AddTokenAsync(t, Ct);
        Assert.Equal(t, await store.FindByAccessTokenHashAsync("ah1", Ct));
    }

    [Fact]
    public async Task FindByAccessTokenHash_unknown_returns_null()
    {
        var store = new InMemoryTokenStore();
        Assert.Null(await store.FindByAccessTokenHashAsync("nope", Ct));
    }

    [Fact]
    public async Task AddToken_duplicate_access_hash_throws()
    {
        var store = new InMemoryTokenStore();
        var t = NewToken("ah1", "rh1");
        await store.AddTokenAsync(t, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddTokenAsync(t, Ct).AsTask());
    }

    [Fact]
    public async Task FindByRefreshTokenHash_returns_match_and_null()
    {
        var store = new InMemoryTokenStore();
        await store.AddTokenAsync(NewToken("ah1", "rh1"), Ct);
        await store.AddTokenAsync(NewToken("ah2", "rh2"), Ct);

        var found = await store.FindByRefreshTokenHashAsync("rh2", Ct);
        Assert.NotNull(found);
        Assert.Equal("ah2", found!.AccessTokenHash);

        Assert.Null(await store.FindByRefreshTokenHashAsync("rh3", Ct));
    }

    [Fact]
    public async Task RevokeToken_removes_by_refresh_hash()
    {
        var store = new InMemoryTokenStore();
        await store.AddTokenAsync(NewToken("ah1", "rh1"), Ct);
        await store.RevokeTokenAsync("rh1", Ct);
        Assert.Null(await store.FindByAccessTokenHashAsync("ah1", Ct));
        Assert.Null(await store.FindByRefreshTokenHashAsync("rh1", Ct));
    }

    [Fact]
    public async Task RevokeToken_unknown_hash_is_noop()
    {
        var store = new InMemoryTokenStore();
        await store.AddTokenAsync(NewToken("ah1", "rh1"), Ct);
        await store.RevokeTokenAsync("rh-missing", Ct);
        Assert.NotNull(await store.FindByAccessTokenHashAsync("ah1", Ct));
    }

    [Fact]
    public async Task ReplaceToken_swaps_old_for_new()
    {
        var store = new InMemoryTokenStore();
        var oldT = NewToken("ah1", "rh1");
        var newT = NewToken("ah2", "rh2");
        await store.AddTokenAsync(oldT, Ct);
        await store.ReplaceTokenAsync(oldT, newT, Ct);
        Assert.Null(await store.FindByAccessTokenHashAsync("ah1", Ct));
        Assert.Equal(newT, await store.FindByAccessTokenHashAsync("ah2", Ct));
    }

    [Fact]
    public async Task ReplaceToken_collision_on_new_hash_throws()
    {
        var store = new InMemoryTokenStore();
        var oldT = NewToken("ah1", "rh1");
        var other = NewToken("ah2", "rh2");
        await store.AddTokenAsync(oldT, Ct);
        await store.AddTokenAsync(other, Ct);
        var dupe = NewToken("ah2", "rh3");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceTokenAsync(oldT, dupe, Ct).AsTask());
    }

    [Fact]
    public async Task AddAuthorizationCode_duplicate_throws()
    {
        var store = new InMemoryTokenStore();
        var rec = new AuthorizationCodeRecord(
            "code", "cid", "https://x/cb", "ch", "S256", "ZW5j", "oid", "upn@x",
            DateTimeOffset.UtcNow.AddSeconds(60));
        await store.AddAuthorizationCodeAsync(rec, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAuthorizationCodeAsync(rec, Ct).AsTask());
    }
}
