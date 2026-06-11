using AdoMcpBridge.Core.Data;
using AdoMcpBridge.Core.OAuth;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.Data;

[Collection("SqlServer")]
public sealed class EfAuthorizationSessionCacheTests
{
    private readonly SqlServerFixture _fx;
    public EfAuthorizationSessionCacheTests(SqlServerFixture fx) => _fx = fx;

    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private EfAuthorizationSessionCache NewCache(DateTimeOffset? now = null)
    {
        var opts = new DbContextOptionsBuilder<BridgeDbContext>()
            .UseSqlServer(_fx.ConnectionString)
            .Options;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now ?? Now);
        return new EfAuthorizationSessionCache(new BridgeDbContext(opts), clock);
    }

    private static AuthorizationSession Session(string id, string entraState, DateTimeOffset expires) => new(
        id, "client-1", "http://127.0.0.1:1/cb", "challenge", "S256",
        "client-state", "entra-verifier", entraState, expires);

    [SkippableFact]
    public async Task Put_then_get_survives_a_separate_context()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var id = Guid.NewGuid().ToString("N");
        var s = Session(id, $"es-{id}", Now.AddMinutes(10));

        await NewCache().PutAsync(s, CancellationToken.None);
        // A different cache instance = different DbContext = "different
        // replica" — the loss mode issue #33 exists to close.
        var got = await NewCache().GetAsync(id, CancellationToken.None);

        Assert.Equal(s, got);
    }

    [SkippableFact]
    public async Task GetByEntraState_finds_the_session_across_contexts()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var id = Guid.NewGuid().ToString("N");
        var s = Session(id, $"es-{id}", Now.AddMinutes(10));

        await NewCache().PutAsync(s, CancellationToken.None);
        var got = await NewCache().GetByEntraStateAsync($"es-{id}", CancellationToken.None);

        Assert.Equal(s, got);
    }

    [SkippableFact]
    public async Task Expired_sessions_return_null_and_are_purged()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var id = Guid.NewGuid().ToString("N");
        await NewCache().PutAsync(Session(id, $"es-{id}", Now.AddMinutes(5)), CancellationToken.None);

        var later = NewCache(now: Now.AddMinutes(6));
        Assert.Null(await later.GetAsync(id, CancellationToken.None));
        Assert.Null(await NewCache(now: Now.AddMinutes(6)).GetByEntraStateAsync($"es-{id}", CancellationToken.None));
    }

    [SkippableFact]
    public async Task Unknown_session_and_state_return_null()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        Assert.Null(await NewCache().GetAsync("nope", CancellationToken.None));
        Assert.Null(await NewCache().GetByEntraStateAsync("nope", CancellationToken.None));
    }

    [SkippableFact]
    public async Task Remove_consumes_the_session()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var id = Guid.NewGuid().ToString("N");
        await NewCache().PutAsync(Session(id, $"es-{id}", Now.AddMinutes(10)), CancellationToken.None);

        await NewCache().RemoveAsync(id, CancellationToken.None);
        Assert.Null(await NewCache().GetAsync(id, CancellationToken.None));

        // Removing again is a no-op, not an error.
        await NewCache().RemoveAsync(id, CancellationToken.None);
    }
}
