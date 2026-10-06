using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Runs a saved query by id and returns work item IDs only, in the same slim shape as
/// <see cref="WiqlQueryTool"/> (whose result slimmer it reuses).
/// </summary>
internal sealed class WitRunQueryTool : ICustomMcpTool
{
    internal const int DefaultTop = WiqlQueryTool.DefaultTop;
    internal const int MaxTop = WiqlQueryTool.MaxTop;

    private readonly IAdoRestClient _ado;
    private readonly ILogger<WitRunQueryTool> _logger;

    public WitRunQueryTool(IAdoRestClient ado, ILogger<WitRunQueryTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_run_query";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Runs a saved query by its id (find ids with ado_bridge_wit_list_queries). " +
        "Returns work item IDs only, plus 'truncated' when more than 'top' matched — hydrate fields with " +
        "ado_bridge_wit_get_batch. Pass team for queries that use @CurrentIteration.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            queryId = new { type = "string", description = "Saved query id (GUID); folders cannot be run." },
            team = new
            {
                type = "string",
                description = "ADO team name (optional). Required for @CurrentIteration macros.",
            },
            top = new
            {
                type = "integer",
                description = $"Maximum number of results to return (default {DefaultTop}, max {MaxTop}).",
            },
        },
        required = new[] { "organization", "project", "queryId" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var queryIdText = ToolArgs.RequireString(arguments, "queryId");
        if (!Guid.TryParse(queryIdText, out var queryId))
            throw new CallerArgumentException("'queryId' must be a saved query GUID.");
        var team = ToolArgs.GetString(arguments, "team") is { } t && !string.IsNullOrWhiteSpace(t) ? t : null;
        var top = ToolArgs.GetInt(arguments, "top") ?? DefaultTop;
        if (top is < 1 or > MaxTop)
            throw new CallerArgumentException($"'top' must be between 1 and {MaxTop}.");

        _logger.LogInformation(
            "ado_bridge_wit_run_query: {Org}/{Project} team={Team} query={QueryId} top={Top}",
            org, project, team ?? "(none)", queryId, top);

        JsonElement result;
        try
        {
            // Request one extra so we can detect (and flag) truncation past top.
            result = await _ado.RunSavedQueryAsync(org, project, team, queryId, top + 1, ct)
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

        return new McpToolResult(WiqlQueryTool.BuildSlimJson(result, top));
    }
}
