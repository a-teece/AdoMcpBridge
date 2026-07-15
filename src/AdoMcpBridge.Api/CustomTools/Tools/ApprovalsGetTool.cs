using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class ApprovalsGetTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly ILogger<ApprovalsGetTool> _logger;

    public ApprovalsGetTool(IAdoRestClient ado, ILogger<ApprovalsGetTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_approvals_get";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Gets a single Azure DevOps pipeline stage/check approval by id, authenticated as the " +
        "signed-in user. Returns a compact projection (id, status, executionOrder, minRequiredApprovers, " +
        "blockedApprovers, and — when expand=steps — the per-approver steps). expand defaults to 'steps'.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            approvalId = new { type = "string", description = "Approval id (uuid)." },
            expand = new
            {
                type = "string",
                @enum = new[] { "none", "steps", "permissions" },
                description = "Expansion level. Defaults to 'steps' (per-approver detail) when omitted.",
            },
        },
        required = new[] { "organization", "project", "approvalId" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var approvalId = arguments.GetProperty("approvalId").GetString()!;
        var expand = arguments.TryGetProperty("expand", out var expandEl) ? expandEl.GetString()! : "steps";

        _logger.LogInformation(
            "ado_bridge_approvals_get: {Org}/{Project} approval {ApprovalId} expand={Expand}",
            org, project, approvalId, expand);

        JsonElement? approval;
        try
        {
            approval = await _ado.GetApprovalAsync(org, project, approvalId, expand, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        if (approval is null)
            return new McpToolResult($"Approval '{approvalId}' not found.", IsError: true);

        return new McpToolResult(ApprovalProjection.ProjectToJson(approval.Value));
    }
}
