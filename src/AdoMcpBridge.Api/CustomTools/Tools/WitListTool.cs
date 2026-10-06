using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Structured work-item listing ("my work", "active bugs in area X", ...) without hand-written
/// WIQL: the filter is compiled by <see cref="WiqlBuilder"/>, run through the WIQL endpoint, then
/// hydrated with the work-items batch API and returned in the slim projection.
/// </summary>
internal sealed class WitListTool : ICustomMcpTool
{
    internal const int DefaultTop = 50;
    internal const int MaxTop = 200;
    internal const string DefaultOrderBy = "System.ChangedDate desc";

    // workitemsbatch accepts at most 200 ids per call.
    private const int HydrateBatchSize = 200;

    private readonly IAdoRestClient _ado;
    private readonly IWorkItemFieldTypeCache _fieldTypes;
    private readonly ILogger<WitListTool> _logger;

    public WitListTool(IAdoRestClient ado, IWorkItemFieldTypeCache fieldTypes, ILogger<WitListTool> logger)
    {
        _ado = ado;
        _fieldTypes = fieldTypes;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_list";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Lists work items in one project matching structured filters (types, states, " +
        "assignedTo — use \"@me\" for the signed-in user —, area/iteration path, tags, title text) without " +
        "writing WIQL. Returns slim work items (long-text fields stubbed) in the requested order, plus " +
        "'truncated' when more than 'top' matched. For arbitrary WIQL use ado_bridge_wiql_query.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            types = StringArray("Work item types to include (e.g. Bug, Task); any of."),
            states = StringArray("States to include (e.g. Active, New); any of."),
            assignedTo = new
            {
                type = "string",
                description = "Assignee: \"@me\" for the signed-in user, or a display name / email.",
            },
            areaPath = new { type = "string", description = "Area path; matches it and everything under it." },
            iterationPath = new { type = "string", description = "Iteration path; matches it and everything under it." },
            tags = StringArray("Tags the work item must carry; all of."),
            textContains = new { type = "string", description = "Text the work item title must contain." },
            top = new
            {
                type = "integer",
                description = $"Maximum number of work items to return (default {DefaultTop}; values above {MaxTop} are capped at {MaxTop}).",
            },
            orderBy = new
            {
                type = "string",
                description = $"Sort order (default \"{DefaultOrderBy}\"): one of " +
                    $"{string.Join(", ", WiqlBuilder.OrderByFields)}, optionally followed by asc or desc " +
                    "(asc when omitted).",
            },
        },
        required = new[] { "organization", "project" },
    };

    private static object StringArray(string description)
        => new { type = "array", items = new { type = "string" }, description };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var top = ToolArgs.GetInt(arguments, "top") ?? DefaultTop;
        if (top < 1)
            throw new CallerArgumentException("'top' must be at least 1.");
        top = Math.Min(top, MaxTop);

        var wiql = WiqlBuilder.Build(new WiqlFilter(
            project,
            GetStringArray(arguments, "types"),
            GetStringArray(arguments, "states"),
            GetNonBlank(arguments, "assignedTo"),
            GetNonBlank(arguments, "areaPath"),
            GetNonBlank(arguments, "iterationPath"),
            GetStringArray(arguments, "tags"),
            GetNonBlank(arguments, "textContains"),
            GetNonBlank(arguments, "orderBy") ?? DefaultOrderBy));

        _logger.LogInformation("ado_bridge_wit_list: {Org}/{Project} top={Top}", org, project, top);

        List<JsonElement> workItems = [];
        IReadOnlySet<string> longTextFields = new HashSet<string>();
        bool truncated;
        try
        {
            // Request one extra so we can detect (and flag) truncation past top.
            var result = await _ado.QueryByWiqlAsync(org, project, null, wiql, top + 1, null, ct)
                .ConfigureAwait(false);

            var ids = result.TryGetProperty("workItems", out var wis)
                ? wis.EnumerateArray().Select(wi => wi.GetProperty("id").GetInt32()).ToList()
                : [];
            truncated = ids.Count > top;
            ids = ids.Take(top).ToList();

            if (ids.Count > 0)
            {
                var witsTask = HydrateAsync(org, project, ids, ct);
                var typesTask = _fieldTypes.GetLongTextFieldRefNamesAsync(org, ct);
                await Task.WhenAll(witsTask, typesTask).ConfigureAwait(false);
                var byId = await witsTask;
                longTextFields = await typesTask;

                // Preserve WIQL order; skip any id deleted between the query and the hydrate.
                foreach (var id in ids)
                    if (byId.TryGetValue(id, out var wi))
                        workItems.Add(wi);
            }
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

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", workItems.Count);
            writer.WriteBoolean("truncated", truncated);
            writer.WritePropertyName("workItems");
            writer.WriteStartArray();
            foreach (var wi in workItems)
                WorkItemSlimProjector.Write(writer, wi, longTextFields);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return new McpToolResult(Encoding.UTF8.GetString(ms.ToArray()));
    }

    private async Task<Dictionary<int, JsonElement>> HydrateAsync(
        string org, string project, List<int> ids, CancellationToken ct)
    {
        var byId = new Dictionary<int, JsonElement>();
        foreach (var chunk in ids.Chunk(HydrateBatchSize))
            foreach (var wi in await _ado.GetWorkItemsBatchAsync(org, project, chunk, ct).ConfigureAwait(false))
                byId[wi.GetProperty("id").GetInt32()] = wi;
        return byId;
    }

    private static string? GetNonBlank(JsonElement args, string name)
        => ToolArgs.GetString(args, name) is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static List<string>? GetStringArray(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;

        if (el.ValueKind != JsonValueKind.Array)
            throw new CallerArgumentException($"'{name}' must be an array of non-empty strings.");

        return el.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())
                ? e.GetString()!
                : throw new CallerArgumentException($"'{name}' must be an array of non-empty strings."))
            .ToList();
    }
}
