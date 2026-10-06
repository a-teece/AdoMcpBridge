using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Native full-text work-item search over the ADO Search API (almsearch), returning a slim
/// projection per hit so callers can triage before hydrating with ado_bridge_wit_get_batch.
/// </summary>
internal sealed class WitSearchTool : ICustomMcpTool
{
    internal const int DefaultTop = 25;
    internal const int MaxTop = 100;

    // ADO Search rejects $skip outside [0, 1000] with HTTP 400 (verified live 2026-10-06).
    internal const int MaxSkip = 1000;

    private readonly IAdoRestClient _ado;
    private readonly ILogger<WitSearchTool> _logger;

    public WitSearchTool(IAdoRestClient ado, ILogger<WitSearchTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_search";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Full-text search of work items (title, description, comments and other " +
        "text fields) via Azure DevOps Search, org-wide or within one project. Returns a slim " +
        "list per hit (id, type, title, state, assignedTo, project) plus the total match count; " +
        "page with top/skip and hydrate full fields with ado_bridge_wit_get_batch.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name (optional; omit to search the whole organisation)." },
            searchText = new { type = "string", description = "Text to search for (Azure DevOps Search syntax)." },
            top = new
            {
                type = "integer",
                description = $"Maximum number of results to return (default {DefaultTop}; values above {MaxTop} are capped at {MaxTop}).",
            },
            skip = new
            {
                type = "integer",
                description = $"Number of results to skip for paging (default 0, max {MaxSkip}).",
            },
        },
        required = new[] { "organization", "searchText" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var searchText = ToolArgs.RequireString(arguments, "searchText");
        var project = ToolArgs.GetString(arguments, "project");
        var top = ToolArgs.GetInt(arguments, "top") ?? DefaultTop;
        var skip = ToolArgs.GetInt(arguments, "skip") ?? 0;

        if (top < 1)
            throw new CallerArgumentException("'top' must be at least 1.");
        if (skip is < 0 or > MaxSkip)
            throw new CallerArgumentException(
                $"'skip' must be between 0 and {MaxSkip} (the Azure DevOps Search paging limit); " +
                "narrow searchText to reach later results.");

        top = Math.Min(top, MaxTop);

        _logger.LogInformation(
            "ado_bridge_wit_search: {Org} project={Project} top={Top} skip={Skip}",
            org, project ?? "(none)", top, skip);

        JsonElement result;
        try
        {
            result = await _ado.SearchWorkItemsAsync(org, project, searchText, top, skip, ct)
                .ConfigureAwait(false);
        }
        catch (AdoRestException ex)
        {
            return new McpToolResult(
                $"Azure DevOps returned HTTP {ex.StatusCode}: {ex.Message}", IsError: true);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed (transport): {ex.Message}", IsError: true);
        }

        return new McpToolResult(BuildSlimJson(result, top, skip));
    }

    internal static string BuildSlimJson(JsonElement result, int top, int skip)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count",
                result.TryGetProperty("count", out var count) ? count.GetInt32() : 0);
            writer.WriteNumber("top", top);
            writer.WriteNumber("skip", skip);

            writer.WritePropertyName("results");
            writer.WriteStartArray();
            if (result.TryGetProperty("results", out var hits))
                foreach (var hit in hits.EnumerateArray())
                    WriteHit(writer, hit);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteHit(Utf8JsonWriter writer, JsonElement hit)
    {
        var fields = hit.TryGetProperty("fields", out var f) ? f : default;

        writer.WriteStartObject();

        // Search returns system.id as a string; project it as a number like the other tools.
        if (int.TryParse(Field(fields, "system.id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            writer.WriteNumber("id", id);
        else
            writer.WriteNull("id");

        WriteNullable(writer, "type", Field(fields, "system.workitemtype"));
        WriteNullable(writer, "title", Field(fields, "system.title"));
        WriteNullable(writer, "state", Field(fields, "system.state"));
        WriteNullable(writer, "assignedTo", Field(fields, "system.assignedto"));

        string? project = null;
        if (hit.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.Object &&
            p.TryGetProperty("name", out var name))
            project = name.GetString();
        WriteNullable(writer, "project", project);

        writer.WriteEndObject();
    }

    private static string? Field(JsonElement fields, string key)
        => fields.ValueKind == JsonValueKind.Object &&
           fields.TryGetProperty(key, out var v) &&
           v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // Search returns "" for unset fields (e.g. an unassigned item); surface those as null.
    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }
}
