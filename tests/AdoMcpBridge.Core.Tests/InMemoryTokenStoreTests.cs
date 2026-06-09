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
}
