using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class ApprovalsApproveTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly ILogger<ApprovalsApproveTool> _logger;

    public ApprovalsApproveTool(IAdoRestClient ado, ILogger<ApprovalsApproveTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_approvals_approve";
    public object? Annotations => new { readOnlyHint = false };

    public string Description =>
        "Write operations: Approves an Azure DevOps pipeline stage/check approval as the signed-in user. ADO " +
        "enforces that the caller is an assigned approver. If the check requires multiple approvers, a single " +
        "approve leaves the approval in 'pending' until the quorum is met — the returned status reflects the " +
        "real outcome (do not assume 'approved'). Optionally include a comment shown to the pipeline. Returns " +
        "the compact projection of the resulting approval.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            approvalId = new { type = "string", description = "Approval id (uuid) to approve." },
            comment = new { type = "string", description = "Optional comment recorded with the approval." },
        },
        required = new[] { "organization", "project", "approvalId" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var approvalId = arguments.GetProperty("approvalId").GetString()!;
        var comment = arguments.TryGetProperty("comment", out var commentEl) ? commentEl.GetString() : null;

        _logger.LogInformation(
            "ado_bridge_approvals_approve: {Org}/{Project} approval {ApprovalId}", org, project, approvalId);

        IReadOnlyList<JsonElement> updated;
        try
        {
            updated = await _ado
                .UpdateApprovalsAsync(org, project, [new ApprovalUpdate(approvalId, "approved", comment)], ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        if (updated.Count == 0)
            return new McpToolResult(
                $"Approval '{approvalId}' update returned no result.", IsError: true);

        return new McpToolResult(ApprovalProjection.ProjectToJson(updated[0]));
    }
}
