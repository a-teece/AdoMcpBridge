using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class ApprovalsRejectTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly ILogger<ApprovalsRejectTool> _logger;

    public ApprovalsRejectTool(IAdoRestClient ado, ILogger<ApprovalsRejectTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_approvals_reject";
    public object? Annotations => new { readOnlyHint = false };

    public string Description =>
        "Write operations: Rejects an Azure DevOps pipeline stage/check approval as the signed-in user. ADO " +
        "enforces that the caller is an assigned approver. Pass a comment explaining the rejection — ADO shows " +
        "it to the pipeline author, so a clear reason is strongly encouraged. Returns the compact projection of " +
        "the resulting approval (its status reflects the real outcome).";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            approvalId = new { type = "string", description = "Approval id (uuid) to reject." },
            comment = new
            {
                type = "string",
                description = "Reason for rejection, shown to the pipeline author. Strongly encouraged.",
            },
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
            "ado_bridge_approvals_reject: {Org}/{Project} approval {ApprovalId}", org, project, approvalId);

        IReadOnlyList<JsonElement> updated;
        try
        {
            updated = await _ado
                .UpdateApprovalsAsync(org, project, [new ApprovalUpdate(approvalId, "rejected", comment)], ct)
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
