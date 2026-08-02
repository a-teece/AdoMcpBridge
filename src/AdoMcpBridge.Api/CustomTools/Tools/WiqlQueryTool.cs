using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class WiqlQueryTool : ICustomMcpTool
{
    internal const int DefaultTop = 200;
    internal const int MaxTop = 2000;

    private readonly IAdoRestClient _ado;
    private readonly ILogger<WiqlQueryTool> _logger;

    public WiqlQueryTool(IAdoRestClient ado, ILogger<WiqlQueryTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_wiql_query";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Executes ad-hoc WIQL text (the upstream wit_query tool only runs saved queries). " +
        "Returns work item IDs only — hydrate fields with ado_bridge_wit_get_batch. " +
        "Supports flat and link (tree/one-hop) queries; pass team for @CurrentIteration macros.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            wiql = new { type = "string", description = "Ad-hoc WIQL query text to execute." },
            project = new { type = "string", description = "ADO project name (optional; scopes the query to a project)." },
            team = new
            {
                type = "string",
                description = "ADO team name (optional; only valid together with project). " +
                    "Required for @CurrentIteration macros.",
            },
            top = new
            {
                type = "integer",
                description = $"Maximum number of results to return (default {DefaultTop}, max {MaxTop}).",
            },
            timePrecision = new
            {
                type = "boolean",
                description = "When true, WIQL date/time comparisons include the time component.",
            },
        },
        required = new[] { "organization", "wiql" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.TryGetProperty("organization", out var orgEl) ? orgEl.GetString() : null;
        var wiql = arguments.TryGetProperty("wiql", out var wiqlEl) ? wiqlEl.GetString() : null;
        var project = arguments.TryGetProperty("project", out var projEl) ? projEl.GetString() : null;
        var team = arguments.TryGetProperty("team", out var teamEl) ? teamEl.GetString() : null;
        int? top = arguments.TryGetProperty("top", out var topEl) ? topEl.GetInt32() : null;
        bool? timePrecision = arguments.TryGetProperty("timePrecision", out var tpEl)
            ? tpEl.GetBoolean()
            : null;

        if (string.IsNullOrWhiteSpace(org))
            return new McpToolResult("organization is required.", IsError: true);
        if (string.IsNullOrWhiteSpace(wiql))
            return new McpToolResult("wiql is required.", IsError: true);
        if (!string.IsNullOrWhiteSpace(team) && string.IsNullOrWhiteSpace(project))
            return new McpToolResult("team is only valid together with project.", IsError: true);
        if (top is < 1 or > MaxTop)
            return new McpToolResult($"top must be between 1 and {MaxTop}.", IsError: true);

        var effectiveTop = top ?? DefaultTop;

        _logger.LogInformation(
            "ado_bridge_wiql_query: {Org} project={Project} team={Team} top={Top}",
            org, project ?? "(none)", team ?? "(none)", effectiveTop);

        JsonElement result;
        try
        {
            // Request one extra so we can detect (and flag) truncation past effectiveTop.
            result = await _ado
                .QueryByWiqlAsync(org, project, team, wiql!, effectiveTop + 1, timePrecision, ct)
                .ConfigureAwait(false);
        }
        catch (AdoWiqlQueryException ex)
        {
            return new McpToolResult($"WIQL query rejected by Azure DevOps: {ex.Message}", IsError: true);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        return new McpToolResult(BuildSlimJson(result, effectiveTop));
    }

    internal static string BuildSlimJson(JsonElement result, int effectiveTop)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            WriteSlim(writer, result, effectiveTop);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteSlim(Utf8JsonWriter writer, JsonElement result, int effectiveTop)
    {
        writer.WriteStartObject();
        WriteEnvelope(writer, result);

        var isLink = result.TryGetProperty("workItemRelations", out var relations);
        if (isLink)
        {
            var items = relations.EnumerateArray().ToList();
            var truncated = items.Count > effectiveTop;
            if (truncated) items = items.Take(effectiveTop).ToList();

            writer.WriteNumber("count", items.Count);
            writer.WriteBoolean("truncated", truncated);
            writer.WritePropertyName("workItemRelations");
            writer.WriteStartArray();
            foreach (var rel in items)
                WriteRelation(writer, rel);
            writer.WriteEndArray();
        }
        else
        {
            var items = result.TryGetProperty("workItems", out var wis)
                ? wis.EnumerateArray().ToList()
                : [];
            var truncated = items.Count > effectiveTop;
            if (truncated) items = items.Take(effectiveTop).ToList();

            writer.WriteNumber("count", items.Count);
            writer.WriteBoolean("truncated", truncated);
            writer.WritePropertyName("workItems");
            writer.WriteStartArray();
            foreach (var wi in items)
                writer.WriteNumberValue(wi.GetProperty("id").GetInt32());
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteEnvelope(Utf8JsonWriter writer, JsonElement result)
    {
        if (result.TryGetProperty("queryType", out var queryType))
            writer.WriteString("queryType", queryType.GetString());
        if (result.TryGetProperty("queryResultType", out var queryResultType))
            writer.WriteString("queryResultType", queryResultType.GetString());
        if (result.TryGetProperty("asOf", out var asOf))
            writer.WriteString("asOf", asOf.GetString());

        writer.WritePropertyName("columns");
        writer.WriteStartArray();
        if (result.TryGetProperty("columns", out var columns))
            foreach (var col in columns.EnumerateArray())
                if (col.TryGetProperty("referenceName", out var refName))
                    writer.WriteStringValue(refName.GetString());
        writer.WriteEndArray();
    }

    private static void WriteRelation(Utf8JsonWriter writer, JsonElement rel)
    {
        writer.WriteStartObject();

        if (rel.TryGetProperty("rel", out var relName) && relName.ValueKind == JsonValueKind.String)
            writer.WriteString("rel", relName.GetString());
        else
            writer.WriteNull("rel");

        if (rel.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
            writer.WriteNumber("source", source.GetProperty("id").GetInt32());
        else
            writer.WriteNull("source");

        if (rel.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Object)
            writer.WriteNumber("target", target.GetProperty("id").GetInt32());
        else
            writer.WriteNull("target");

        writer.WriteEndObject();
    }
}
