using AdoMcpBridge.Core.OAuth;
using FluentAssertions;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.OAuth;

public sealed class InMemoryAuthorizationSessionCacheTests
{
    [Fact]
    public async Task Put_then_get_round_trips()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero));
        var cache = new InMemoryAuthorizationSessionCache(clock);

        var s = new AuthorizationSession("sid", "c1", "https://cb/x", "chal", "S256",
            "client-state", "entra-verifier", "entra-state", clock.UtcNow.AddMinutes(10));
        await cache.PutAsync(s, default);

        var got = await cache.GetAsync("sid", default);
        got.Should().Be(s);
    }

    [Fact]
    public async Task Get_returns_null_after_expiry()
    {
        var clock = Substitute.For<IClock>();
        var now = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);
        var cache = new InMemoryAuthorizationSessionCache(clock);
        var s = new AuthorizationSession("sid", "c", "u", "ch", "S256", "st", "ev", "es",
            now.AddSeconds(1));
        await cache.PutAsync(s, default);

        clock.UtcNow.Returns(now.AddSeconds(2));
        (await cache.GetAsync("sid", default)).Should().BeNull();
    }

    [Fact]
    public async Task GetByEntraState_finds_the_matching_session()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero));
        var cache = new InMemoryAuthorizationSessionCache(clock);
        var s = new AuthorizationSession("sid", "c1", "https://cb/x", "chal", "S256",
            "client-state", "entra-verifier", "entra-state-XYZ", clock.UtcNow.AddMinutes(10));
        await cache.PutAsync(s, default);

        (await cache.GetByEntraStateAsync("entra-state-XYZ", default)).Should().Be(s);
    }

    [Fact]
    public async Task GetByEntraState_returns_null_for_unknown_state()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var cache = new InMemoryAuthorizationSessionCache(clock);
        var s = new AuthorizationSession("sid", "c", "u", "ch", "S256", "st", "ev", "es",
            clock.UtcNow.AddMinutes(5));
        await cache.PutAsync(s, default);

        (await cache.GetByEntraStateAsync("no-such-state", default)).Should().BeNull();
    }

    [Fact]
    public async Task GetByEntraState_returns_null_after_expiry()
    {
        var clock = Substitute.For<IClock>();
        var now = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);
        var cache = new InMemoryAuthorizationSessionCache(clock);
        var s = new AuthorizationSession("sid", "c", "u", "ch", "S256", "st", "ev", "es",
            now.AddSeconds(1));
        await cache.PutAsync(s, default);

        clock.UtcNow.Returns(now.AddSeconds(2));
        (await cache.GetByEntraStateAsync("es", default)).Should().BeNull();
    }

    [Fact]
    public async Task Remove_consumes_session()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var cache = new InMemoryAuthorizationSessionCache(clock);
        var s = new AuthorizationSession("sid", "c", "u", "ch", "S256", "st", "ev", "es",
            clock.UtcNow.AddMinutes(5));
        await cache.PutAsync(s, default);
        await cache.RemoveAsync("sid", default);
        (await cache.GetAsync("sid", default)).Should().BeNull();
    }
}
