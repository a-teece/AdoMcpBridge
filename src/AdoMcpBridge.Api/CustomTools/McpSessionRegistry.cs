using System.Collections.Concurrent;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Tracks the MCP session ids observed by THIS process so the bridge can emit a
/// single <c>notifications/tools/list_changed</c> to sessions that predate a redeploy.
///
/// <para>
/// Process identity is the deploy detector. A freshly started process begins with an
/// empty registry, so any session id it never saw <c>initialize</c> for must have been
/// created against an earlier process (i.e. before this deploy) and is therefore holding
/// a stale tool list. Sessions "born" in this process (their <c>initialize</c> passed
/// through here) already received the current tool schema and must never be notified.
/// No version numbers are needed.
/// </para>
///
/// <para>
/// Multi-replica caveat: each replica keeps its own registry, so a session that hops
/// replicas may be notified once per replica that has not seen it. Duplicate refreshes
/// are harmless — the client simply re-fetches tools again.
/// </para>
/// </summary>
internal interface IMcpSessionRegistry
{
    /// <summary>Records a session id whose <c>initialize</c> was handled by this process.</summary>
    void MarkBorn(string sessionId);

    /// <summary>
    /// True iff <paramref name="sessionId"/> is non-empty, was NOT born in this process,
    /// and has not already been notified — i.e. it predates this deploy and still needs
    /// exactly one refresh hint.
    /// </summary>
    bool ShouldNotify(string sessionId);

    /// <summary>Records that a stale session has been sent its one refresh notification.</summary>
    void MarkNotified(string sessionId);
}

/// <inheritdoc cref="IMcpSessionRegistry"/>
internal sealed class McpSessionRegistry : IMcpSessionRegistry
{
    /// <summary>
    /// Upper bound on tracked session ids. The registry only ever grows for the process
    /// lifetime, so this cap keeps memory bounded under heavy session churn. When the cap
    /// is reached the registry stops recording NEW ids and treats every unknown id as
    /// "already known" (see <see cref="ShouldNotify"/>): a deliberate fail-safe that
    /// prefers missing a refresh notification over unbounded memory growth or notification
    /// spam. 10k session ids is far more than a single replica sees between deploys.
    /// </summary>
    internal const int MaxEntries = 10_000;

    // Presence means "do not notify" — the key was either born in this process or has
    // already been notified. Both states collapse to the same suppression behaviour, so a
    // single set (ordinal string comparison for exact session-id matching) is sufficient.
    private readonly ConcurrentDictionary<string, byte> _known = new(StringComparer.Ordinal);

    public void MarkBorn(string sessionId) => Record(sessionId);

    public void MarkNotified(string sessionId) => Record(sessionId);

    public bool ShouldNotify(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        if (_known.ContainsKey(sessionId)) return false;
        // Unknown id: notify only while we still have room to remember having done so.
        return _known.Count < MaxEntries;
    }

    private void Record(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        // At capacity, refuse to grow — but still no-op silently for ids we already track.
        if (_known.Count >= MaxEntries && !_known.ContainsKey(sessionId)) return;
        _known[sessionId] = 0;
    }
}
