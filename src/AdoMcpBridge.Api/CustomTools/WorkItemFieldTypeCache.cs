using System.Collections.Concurrent;

namespace AdoMcpBridge.Api.CustomTools;

internal interface IWorkItemFieldTypeCache
{
    /// <summary>
    /// Returns the set of field reference names whose ADO type is
    /// <c>html</c>, <c>plainText</c>, or <c>history</c> for the given
    /// organisation. Results are cached for the process lifetime.
    /// </summary>
    Task<IReadOnlySet<string>> GetLongTextFieldRefNamesAsync(
        string org, CancellationToken ct = default);
}

internal sealed class WorkItemFieldTypeCache : IWorkItemFieldTypeCache
{
    // "history" catches System.History, which ADO populates with the most recent
    // revision/comment text — structurally always large, yet typed "history" (not
    // "html"), so it would otherwise slip past the stubber and blow the context budget.
    private static readonly HashSet<string> LongTextTypes =
        new(["html", "plainText", "history"], StringComparer.OrdinalIgnoreCase);

    private readonly IAdoRestClient _ado;
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public WorkItemFieldTypeCache(IAdoRestClient ado) => _ado = ado;

    public async Task<IReadOnlySet<string>> GetLongTextFieldRefNamesAsync(
        string org, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(org, out var cached)) return cached;

        var fetched = await _ado
            .GetFieldRefNamesByTypeAsync(org, LongTextTypes, ct)
            .ConfigureAwait(false);

        // Two concurrent first-calls for the same org may both fetch; GetOrAdd
        // ensures only one value wins and the other is discarded harmlessly.
        return _cache.GetOrAdd(org, fetched);
    }
}
