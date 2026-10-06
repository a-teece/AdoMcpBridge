using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools;

public partial interface IAdoRestClient
{
    /// <summary>
    /// Lists the project's saved-query hierarchy (<c>GET _apis/wit/queries</c>) to
    /// <paramref name="depth"/> levels below the root folders, without query text
    /// (<c>$expand=none</c>). Returns the cloned raw response (<c>count</c>, <c>value[]</c>,
    /// nested items under <c>children</c>). ADO accepts <c>$depth</c> 0–2 only; a
    /// non-success status is surfaced via <see cref="AdoRestException"/>.
    /// </summary>
    Task<JsonElement> ListQueriesAsync(string org, string project, int depth, CancellationToken ct = default);

    /// <summary>
    /// Runs a saved query by id (<c>GET _apis/wit/wiql/{queryId}</c>), team-scoped when
    /// <paramref name="team"/> is supplied (needed for <c>@CurrentIteration</c>).
    /// <paramref name="top"/> maps to <c>$top</c> and is omitted when <see langword="null"/>.
    /// Returns the cloned WorkItemQueryResult — the same shape as
    /// <see cref="QueryByWiqlAsync"/>. An unknown id is a 404 (TF401243) and a folder id a
    /// 400, both surfaced via <see cref="AdoRestException"/>.
    /// </summary>
    Task<JsonElement> RunSavedQueryAsync(
        string org, string project, string? team, Guid queryId, int? top, CancellationToken ct = default);
}

internal sealed partial class AdoRestClient
{
    public async Task<JsonElement> ListQueriesAsync(
        string org, string project, int depth, CancellationToken ct = default)
    {
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}" +
            $"/_apis/wit/queries?{Uri.EscapeDataString("$depth")}={depth}" +
            $"&{Uri.EscapeDataString("$expand")}=none&api-version=7.1";
        return await GetJsonAsync(url, "saved-query list", org, ct).ConfigureAwait(false);
    }

    public async Task<JsonElement> RunSavedQueryAsync(
        string org, string project, string? team, Guid queryId, int? top, CancellationToken ct = default)
    {
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}";
        if (team is not null) url += $"/{Uri.EscapeDataString(team)}";
        url += $"/_apis/wit/wiql/{queryId:D}?api-version=7.1";
        if (top is not null) url += $"&{Uri.EscapeDataString("$top")}={top.Value}";
        return await GetJsonAsync(url, "saved-query run", org, ct).ConfigureAwait(false);
    }

    private async Task<JsonElement> GetJsonAsync(string url, string operation, string org, CancellationToken ct)
    {
        using var req = BuildRequest(HttpMethod.Get, url, null);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("ADO {Operation} in {Org} returned {Status}.", operation, org, (int)res.StatusCode);
            throw new AdoRestException((int)res.StatusCode, ExtractErrorMessage(err, res.StatusCode));
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
