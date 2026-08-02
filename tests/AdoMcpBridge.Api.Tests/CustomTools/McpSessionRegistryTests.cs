using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public sealed class McpSessionRegistryTests
{
    private static McpSessionRegistry Create() => new();

    [Fact]
    public void Unknown_session_should_be_notified()
    {
        var registry = Create();

        registry.ShouldNotify("stale-session").Should().BeTrue();
    }

    [Fact]
    public void Session_born_in_this_process_is_never_notified()
    {
        var registry = Create();

        registry.MarkBorn("fresh-session");

        registry.ShouldNotify("fresh-session").Should().BeFalse();
    }

    [Fact]
    public void Notified_session_is_not_notified_again()
    {
        var registry = Create();

        registry.ShouldNotify("stale").Should().BeTrue();
        registry.MarkNotified("stale");

        registry.ShouldNotify("stale").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_or_null_session_ids_are_never_notified(string? sessionId)
    {
        var registry = Create();

        registry.ShouldNotify(sessionId!).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_or_null_session_ids_are_ignored_by_mark_methods(string? sessionId)
    {
        var registry = Create();

        // Must not throw and must not consume a registry slot.
        registry.MarkBorn(sessionId!);
        registry.MarkNotified(sessionId!);

        registry.ShouldNotify("real-session").Should().BeTrue();
    }

    [Fact]
    public void When_full_unknown_sessions_are_treated_as_known()
    {
        var registry = Create();
        for (var i = 0; i < McpSessionRegistry.MaxEntries; i++)
        {
            registry.MarkBorn($"born-{i}");
        }

        // Registry is full: a never-seen id must not be notified (fail-safe), and
        // recording a new id must be a silent no-op rather than growing the map.
        registry.ShouldNotify("overflow-session").Should().BeFalse();
        registry.MarkNotified("overflow-session");
        registry.ShouldNotify("overflow-session").Should().BeFalse();
    }

    [Fact]
    public void When_full_already_known_ids_are_still_recognised()
    {
        var registry = Create();
        for (var i = 0; i < McpSessionRegistry.MaxEntries - 1; i++)
        {
            registry.MarkBorn($"born-{i}");
        }

        // One slot left: this stale id gets notified and recorded, filling the map.
        registry.ShouldNotify("last-stale").Should().BeTrue();
        registry.MarkNotified("last-stale");

        // Now full — re-recording the SAME id must remain a no-op that keeps it suppressed.
        registry.MarkNotified("last-stale");
        registry.ShouldNotify("last-stale").Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_mark_and_query_are_thread_safe()
    {
        var registry = Create();
        var ids = Enumerable.Range(0, 500).Select(i => $"session-{i}").ToArray();

        await Task.WhenAll(ids.Select(id => Task.Run(() =>
        {
            _ = registry.ShouldNotify(id);
            registry.MarkBorn(id);
            _ = registry.ShouldNotify(id);
            registry.MarkNotified(id);
        })));

        // Every id was marked born, so none should still be notifiable.
        foreach (var id in ids)
        {
            registry.ShouldNotify(id).Should().BeFalse();
        }
    }
}
