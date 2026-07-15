using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class ApprovalsListTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly ILogger<ApprovalsListTool> _logger;

    public ApprovalsListTool(IAdoRestClient ado, ILogger<ApprovalsListTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_approvals_list";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Lists Azure DevOps pipeline stage/check approvals in a project, authenticated as " +
        "the signed-in user. Optionally filter by state, specific approval ids, or assigned user ids, and cap " +
        "the number returned with top. Returns {\"count\":N,\"value\":[approval,...]} where each approval is a " +
        "compact projection (id, status, executionOrder, minRequiredApprovers, blockedApprovers, and — when " +
        "expand=steps — the per-approver steps). expand defaults to 'steps' so approver assignment is visible.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            state = new { type = "string", description = "Filter by approval status (e.g. pending, approved, rejected)." },
            approvalIds = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Filter to specific approval ids (uuids).",
            },
            userIds = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Filter to approvals assigned to these user ids.",
            },
            top = new { type = "integer", description = "Maximum number of approvals to return." },
            expand = new
            {
                type = "string",
                @enum = new[] { "none", "steps", "permissions" },
                description = "Expansion level. Defaults to 'steps' (per-approver detail) when omitted.",
            },
        },
        required = new[] { "organization", "project" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var state = arguments.TryGetProperty("state", out var stateEl) ? stateEl.GetString() : null;
        var approvalIds = ReadStringArray(arguments, "approvalIds");
        var userIds = ReadStringArray(arguments, "userIds");
        int? top = arguments.TryGetProperty("top", out var topEl) ? topEl.GetInt32() : null;
        var expand = arguments.TryGetProperty("expand", out var expandEl) ? expandEl.GetString()! : "steps";

        _logger.LogInformation(
            "ado_bridge_approvals_list: {Org}/{Project} state={State} expand={Expand}",
            org, project, state ?? "(any)", expand);

        IReadOnlyList<JsonElement> approvals;
        try
        {
            approvals = await _ado
                .QueryApprovalsAsync(org, project, approvalIds, state, userIds, top, expand, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", approvals.Count);
            writer.WritePropertyName("value");
            writer.WriteStartArray();
            foreach (var approval in approvals)
                ApprovalProjection.WriteApproval(writer, approval);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return new McpToolResult(Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static List<string>? ReadStringArray(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var array))
            return null;
        return array.EnumerateArray().Select(e => e.GetString()!).ToList();
    }
}
