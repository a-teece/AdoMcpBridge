using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class DownloadFieldTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly ILogger<DownloadFieldTool> _logger;

    public DownloadFieldTool(IAdoRestClient ado, ILogger<DownloadFieldTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_download_field";
    public object? Annotations => new { readOnlyHint = true };
    public string Description =>
        "Read operations: Downloads a large Azure DevOps work-item long-text field " +
        "(e.g. System.Description or Custom.ImplementationPlan) and returns its content as plain markdown. " +
        "ADO entity-encoding is reversed automatically; the caller receives the original markdown.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            workItemId = new { type = "integer", description = "Work-item numeric id." },
            fieldRefName = new { type = "string", description = "Field reference name (e.g. System.Description)." },
        },
        required = new[] { "organization", "project", "workItemId", "fieldRefName" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var workItemId = arguments.GetProperty("workItemId").GetInt32();
        var fieldRef = arguments.GetProperty("fieldRefName").GetString()!;

        _logger.LogInformation(
            "ado_bridge_download_field: WI {Id} field {Field}", workItemId, fieldRef);

        string? raw;
        try
        {
            raw = await _ado.GetFieldAsync(org, project, workItemId, fieldRef, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        if (raw is null)
            return new McpToolResult($"Field '{fieldRef}' not found on work item {workItemId}.", IsError: true);

        var markdown = AdoFieldEscaper.Unescape(raw);
        return new McpToolResult(markdown);
    }
}
