using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools;

public partial interface IAdoRestClient
{
    /// <summary>
    /// Runs a full-text work-item search via the ADO Search API on the <c>almsearch</c> host
    /// (a different host from the <c>dev.azure.com</c> calls). Scope is org-wide, or a single
    /// project when <paramref name="project"/> is supplied. <paramref name="top"/> and
    /// <paramref name="skip"/> map to the body's <c>$top</c> / <c>$skip</c>; facets are not
    /// requested. Returns the cloned raw response (<c>count</c>, <c>results[]</c>). A zero-hit
    /// search is an HTTP 200 with an empty <c>results[]</c>, not an error; a non-success
    /// status is surfaced via <see cref="AdoRestException"/>.
    /// </summary>
    Task<JsonElement> SearchWorkItemsAsync(
        string org, string? project, string searchText, int top, int skip,
        CancellationToken ct = default);
}

internal sealed partial class AdoRestClient
{
    public async Task<JsonElement> SearchWorkItemsAsync(
        string org, string? project, string searchText, int top, int skip,
        CancellationToken ct = default)
    {
        var url = $"https://almsearch.dev.azure.com/{Uri.EscapeDataString(org)}";
        if (project is not null) url += $"/{Uri.EscapeDataString(project)}";
        url += "/_apis/search/workitemsearchresults?api-version=7.1";

        var payload = new Dictionary<string, object>
        {
            ["searchText"] = searchText,
            ["$skip"] = skip,
            ["$top"] = top,
            ["includeFacets"] = false,
        };
        var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var req = BuildRequest(HttpMethod.Post, url, body);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("ADO work item search in {Org} returned {Status}.", org, (int)res.StatusCode);
            throw new AdoRestException((int)res.StatusCode, ExtractErrorMessage(err, res.StatusCode));
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
