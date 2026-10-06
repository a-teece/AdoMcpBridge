using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools;

public partial interface IAdoRestClient
{
    /// <summary>
    /// Applies a JSON-Patch document to one work item
    /// (<c>PATCH {org}/{project}/_apis/wit/workitems/{id}</c>, <c>application/json-patch+json</c>)
    /// and returns the cloned updated work item (its <c>id</c>, <c>rev</c> and <c>fields</c>).
    /// A non-success status (an invalid field value, a rule violation, a missing item, no edit
    /// permission, …) is surfaced via <see cref="AdoRestException"/> carrying ADO's message.
    /// </summary>
    Task<JsonElement> UpdateWorkItemAsync(
        string org, string project, int id, IReadOnlyList<object> patchOps, CancellationToken ct = default);
}

internal sealed partial class AdoRestClient
{
    public async Task<JsonElement> UpdateWorkItemAsync(
        string org, string project, int id, IReadOnlyList<object> patchOps, CancellationToken ct = default)
    {
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(project)}" +
                  $"/_apis/wit/workitems/{id}?api-version=7.1";

        var body = new StringContent(JsonSerializer.Serialize(patchOps), Encoding.UTF8, "application/json-patch+json");

        using var req = BuildRequest(HttpMethod.Patch, url, body);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("ADO PATCH WI {Id} in {Org}/{Project} returned {Status}.",
                id, org, project, (int)res.StatusCode);
            throw new AdoRestException((int)res.StatusCode, ExtractErrorMessage(err, res.StatusCode));
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
