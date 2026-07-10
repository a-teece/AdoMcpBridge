using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;

namespace AdoMcpBridge.Api.CustomTools;

public interface IAdoRestClient
{
    /// <summary>
    /// Reads a single work-item field and returns its raw stored value, or
    /// <see langword="null"/> if the field is absent.
    /// </summary>
    Task<string?> GetFieldAsync(
        string org, string project, int workItemId, string fieldRefName,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces the entire content of a work-item long-text field.
    /// The caller is responsible for any required entity-escaping before
    /// passing <paramref name="escapedValue"/>.
    /// </summary>
    Task PatchFieldAsync(
        string org, string project, int workItemId, string fieldRefName,
        string escapedValue, CancellationToken ct = default);

    /// <summary>
    /// Returns all fields for a single work item (expanded), or
    /// <see langword="null"/> if the item does not exist.
    /// The returned <see cref="JsonElement"/> is independent of any
    /// underlying <see cref="JsonDocument"/> lifetime.
    /// </summary>
    Task<JsonElement?> GetWorkItemAsync(
        string org, string project, int id, CancellationToken ct = default);

    /// <summary>
    /// Returns all fields for each of the requested work items in a single
    /// batch call. The order of results matches the order of <paramref name="ids"/>.
    /// </summary>
    Task<IReadOnlyList<JsonElement>> GetWorkItemsBatchAsync(
        string org, string project, IReadOnlyList<int> ids, CancellationToken ct = default);

    /// <summary>
    /// Returns the set of field reference names whose ADO field type is
    /// contained in <paramref name="types"/> (e.g. <c>"html"</c>,
    /// <c>"plainText"</c>). Comparison is case-insensitive.
    /// </summary>
    Task<IReadOnlySet<string>> GetFieldRefNamesByTypeAsync(
        string org, IReadOnlySet<string> types, CancellationToken ct = default);
}

internal sealed class AdoRestClient : IAdoRestClient
{
    // ADO REST API resource id — scopes tokens for dev.azure.com.
    private static readonly string[] AdoScopes =
        ["499b84ac-1321-427f-aa17-267ca6975798/.default"];

    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly ILogger<AdoRestClient> _logger;

    public AdoRestClient(HttpClient http, TokenCredential credential, ILogger<AdoRestClient> logger)
    {
        _http = http;
        _credential = credential;
        _logger = logger;
    }

    public async Task<string?> GetFieldAsync(
        string org, string project, int workItemId, string fieldRefName,
        CancellationToken ct = default)
    {
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}" +
                   $"/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{workItemId}" +
                   $"?fields={Uri.EscapeDataString(fieldRefName)}&api-version=7.1";

        using var req = await BuildRequestAsync(HttpMethod.Get, url, body: null, ct);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("ADO GET WI {Id} field {Field} returned {Status}: {Body}",
                workItemId, fieldRefName, (int)res.StatusCode, err);
            res.EnsureSuccessStatusCode(); // throws HttpRequestException
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("fields", out var fields) &&
            fields.TryGetProperty(fieldRefName, out var field))
        {
            return field.ValueKind == JsonValueKind.Null ? null : field.GetString();
        }

        return null;
    }

    public async Task PatchFieldAsync(
        string org, string project, int workItemId, string fieldRefName,
        string escapedValue, CancellationToken ct = default)
    {
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}" +
                  $"/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{workItemId}" +
                  $"?api-version=7.1";

        // JSON-Patch body — the serializer handles JSON wire escaping; the
        // entity-escaping of the markdown content is the caller's responsibility.
        var patch = JsonSerializer.Serialize(new[]
        {
            new { op = "replace", path = $"/fields/{fieldRefName}", value = escapedValue }
        });

        using var req = await BuildRequestAsync(
            HttpMethod.Patch, url,
            body: new StringContent(patch, Encoding.UTF8, "application/json-patch+json"),
            ct);

        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("ADO PATCH WI {Id} field {Field} returned {Status}: {Body}",
                workItemId, fieldRefName, (int)res.StatusCode, err);
            res.EnsureSuccessStatusCode();
        }
    }

    public async Task<JsonElement?> GetWorkItemAsync(
        string org, string project, int id, CancellationToken ct = default)
    {
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}" +
                   $"/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{id}" +
                   $"?$expand=All&api-version=7.1";

        using var req = await BuildRequestAsync(HttpMethod.Get, url, body: null, ct);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("ADO GET WI {Id} returned {Status}: {Body}", id, (int)res.StatusCode, err);
            res.EnsureSuccessStatusCode();
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<IReadOnlyList<JsonElement>> GetWorkItemsBatchAsync(
        string org, string project, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}" +
                   $"/{Uri.EscapeDataString(project)}/_apis/wit/workitemsbatch?api-version=7.1";

        var payload = new Dictionary<string, object> { ["ids"] = ids, ["$expand"] = "All" };
        var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var req = await BuildRequestAsync(HttpMethod.Post, url, body, ct);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("ADO WI batch returned {Status}: {Body}", (int)res.StatusCode, err);
            res.EnsureSuccessStatusCode();
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var result = new List<JsonElement>();
        if (doc.RootElement.TryGetProperty("value", out var values))
            foreach (var item in values.EnumerateArray())
                result.Add(item.Clone());

        return result;
    }

    public async Task<IReadOnlySet<string>> GetFieldRefNamesByTypeAsync(
        string org, IReadOnlySet<string> types, CancellationToken ct = default)
    {
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/_apis/wit/fields?api-version=7.1";

        using var req = await BuildRequestAsync(HttpMethod.Get, url, body: null, ct);
        using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("ADO GET fields for {Org} returned {Status}: {Body}", org, (int)res.StatusCode, err);
            res.EnsureSuccessStatusCode();
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.TryGetProperty("value", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (field.TryGetProperty("type", out var typeEl) &&
                    field.TryGetProperty("referenceName", out var refNameEl))
                {
                    var type = typeEl.GetString();
                    var refName = refNameEl.GetString();
                    if (type is not null && refName is not null && types.Contains(type))
                        result.Add(refName);
                }
            }
        }
        return result;
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method, string url, HttpContent? body, CancellationToken ct)
    {
        var tokenResult = await _credential
            .GetTokenAsync(new TokenRequestContext(AdoScopes), ct)
            .ConfigureAwait(false);

        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) req.Content = body;
        return req;
    }
}
