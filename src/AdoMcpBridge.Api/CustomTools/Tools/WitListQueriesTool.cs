using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Lists a project's saved queries and folders as a flat, path-ordered list so a caller can
/// pick a query id for <see cref="WitRunQueryTool"/> without walking ADO's nested tree.
/// </summary>
internal sealed class WitListQueriesTool : ICustomMcpTool
{
    internal const int DefaultDepth = 2;

    // ADO rejects $depth outside 0..2 (live-verified 2026-10-06); 0 returns only the two
    // root folders, which is never useful on its own.
    internal const int MinDepth = 1;
    internal const int MaxDepth = 2;

    private readonly IAdoRestClient _ado;
    private readonly ILogger<WitListQueriesTool> _logger;

    public WitListQueriesTool(IAdoRestClient ado, ILogger<WitListQueriesTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_list_queries";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Lists the saved queries and query folders in one project (Shared Queries and " +
        "My Queries) as a flat list of {id, name, path, isFolder}, folders before their contents. " +
        "Run a query by id with ado_bridge_wit_run_query.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            depth = new
            {
                type = "integer",
                description = $"Folder levels to expand below the root folders ({MinDepth}–{MaxDepth}, default {DefaultDepth}).",
            },
        },
        required = new[] { "organization", "project" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var depth = ToolArgs.GetInt(arguments, "depth") ?? DefaultDepth;
        if (depth is < MinDepth or > MaxDepth)
            throw new CallerArgumentException($"'depth' must be between {MinDepth} and {MaxDepth}.");

        _logger.LogInformation("ado_bridge_wit_list_queries: {Org}/{Project} depth={Depth}", org, project, depth);

        JsonElement result;
        try
        {
            result = await _ado.ListQueriesAsync(org, project, depth, ct).ConfigureAwait(false);
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

        var items = new List<JsonElement>();
        if (result.TryGetProperty("value", out var roots))
            foreach (var root in roots.EnumerateArray())
                Flatten(root, items);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", items.Count);
            writer.WritePropertyName("queries");
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.GetProperty("id").GetString());
                writer.WriteString("name", item.GetProperty("name").GetString());
                writer.WriteString("path", item.GetProperty("path").GetString());
                // Query (non-folder) items omit isFolder entirely.
                writer.WriteBoolean("isFolder",
                    item.TryGetProperty("isFolder", out var isFolder) && isFolder.GetBoolean());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return new McpToolResult(Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static void Flatten(JsonElement node, List<JsonElement> items)
    {
        items.Add(node);
        if (node.TryGetProperty("children", out var children))
            foreach (var child in children.EnumerateArray())
                Flatten(child, items);
    }
}
