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
