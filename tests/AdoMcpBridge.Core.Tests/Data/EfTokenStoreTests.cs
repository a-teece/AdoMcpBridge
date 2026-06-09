using AdoMcpBridge.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace AdoMcpBridge.Core.Tests.Data;

[Collection("SqlServer")]
public sealed class EfTokenStoreTests
{
    private readonly SqlServerFixture _fx;
    public EfTokenStoreTests(SqlServerFixture fx) => _fx = fx;

    private EfTokenStore NewStore()
    {
        var opts = new DbContextOptionsBuilder<BridgeDbContext>()
            .UseSqlServer(_fx.ConnectionString)
            .Options;
        return new EfTokenStore(new BridgeDbContext(opts));
    }

    [SkippableFact]
    public async Task AddClient_then_Find_roundtrips()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        var client = new RegisteredClient(
            ClientId: Guid.NewGuid().ToString("N"),
            ClientName: "claude-code",
            RedirectUris: new[] { "https://example.test/cb" },
            CreatedAt: DateTimeOffset.UtcNow);

        await store.AddClientAsync(client, CancellationToken.None);
        var found = await store.FindClientAsync(client.ClientId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(client.ClientId, found!.ClientId);
        Assert.Equal(client.ClientName, found.ClientName);
        Assert.Equal(client.RedirectUris, found.RedirectUris);
    }

    [SkippableFact]
    public async Task ConsumeAuthorizationCode_returns_and_deletes()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        var code = new AuthorizationCodeRecord(
            Code: Guid.NewGuid().ToString("N"),
            ClientId: "c1",
            RedirectUri: "https://example.test/cb",
            PkceChallenge: "challenge",
            PkceMethod: "S256",
            EntraRefreshTokenEncrypted: "AAAA",
            UserObjectId: "oid",
            UserPrincipalName: "u@example.test",
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(60));

        await store.AddAuthorizationCodeAsync(code, CancellationToken.None);
        var first = await store.ConsumeAuthorizationCodeAsync(code.Code, CancellationToken.None);
        var second = await store.ConsumeAuthorizationCodeAsync(code.Code, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(code.Code, first!.Code);
        Assert.Null(second);
    }

    [SkippableFact]
    public async Task AddToken_FindByAccess_FindByRefresh_Revoke_Replace()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        var token = new TokenRecord(
            AccessTokenHash: NewHash(),
            RefreshTokenHash: NewHash(),
            ClientId: "c1",
            EntraRefreshTokenEncrypted: "AAAA",
            UserObjectId: "oid",
            UserPrincipalName: "u@example.test",
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(14),
            CreatedAt: DateTimeOffset.UtcNow);

        await store.AddTokenAsync(token, CancellationToken.None);

        var byAccess = await store.FindByAccessTokenHashAsync(token.AccessTokenHash, CancellationToken.None);
        var byRefresh = await store.FindByRefreshTokenHashAsync(token.RefreshTokenHash, CancellationToken.None);
        Assert.NotNull(byAccess);
        Assert.NotNull(byRefresh);

        var replacement = token with { AccessTokenHash = NewHash(), RefreshTokenHash = NewHash() };
        await store.ReplaceTokenAsync(token, replacement, CancellationToken.None);

        Assert.Null(await store.FindByAccessTokenHashAsync(token.AccessTokenHash, CancellationToken.None));
        Assert.NotNull(await store.FindByAccessTokenHashAsync(replacement.AccessTokenHash, CancellationToken.None));

        await store.RevokeTokenAsync(replacement.RefreshTokenHash, CancellationToken.None);
        Assert.Null(await store.FindByRefreshTokenHashAsync(replacement.RefreshTokenHash, CancellationToken.None));
    }

    [SkippableFact]
    public async Task FindClient_unknown_returns_null()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        Assert.Null(await store.FindClientAsync(Guid.NewGuid().ToString("N"), CancellationToken.None));
    }

    [SkippableFact]
    public async Task AddClient_with_null_redirects_roundtrips_to_empty_list()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        var client = new RegisteredClient(
            ClientId: Guid.NewGuid().ToString("N"),
            ClientName: "no-redirects",
            RedirectUris: null!,
            CreatedAt: DateTimeOffset.UtcNow);

        await store.AddClientAsync(client, CancellationToken.None);
        var found = await store.FindClientAsync(client.ClientId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Empty(found!.RedirectUris);
    }

    [SkippableFact]
    public async Task RevokeToken_unknown_hash_is_noop()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        await store.RevokeTokenAsync(NewHash(), CancellationToken.None);
    }

    [SkippableFact]
    public async Task ReplaceToken_when_old_absent_adds_new()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        var absent = NewToken();
        var replacement = NewToken();

        await store.ReplaceTokenAsync(absent, replacement, CancellationToken.None);

        Assert.NotNull(await store.FindByAccessTokenHashAsync(replacement.AccessTokenHash, CancellationToken.None));
        Assert.Null(await store.FindByAccessTokenHashAsync(absent.AccessTokenHash, CancellationToken.None));
    }

    private static TokenRecord NewToken() => new(
        AccessTokenHash: NewHash(),
        RefreshTokenHash: NewHash(),
        ClientId: "c1",
        EntraRefreshTokenEncrypted: "AAAA",
        UserObjectId: "oid",
        UserPrincipalName: "u@example.test",
        AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
        RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(14),
        CreatedAt: DateTimeOffset.UtcNow);

    private static string NewHash() =>
        Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant().PadRight(64, '0')[..64];
}
