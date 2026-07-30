using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class GetCommentTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly ILogger<GetCommentTool> _logger;

    public GetCommentTool(IAdoRestClient ado, ILogger<GetCommentTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_get_comment";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Downloads the full body of a single Azure DevOps work-item comment " +
        "(identified by the id from ado_bridge_list_comments) and returns it as plain markdown. " +
        "ADO entity-encoding is reversed automatically. Fetch one comment at a time to stay within budget.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            workItemId = new { type = "integer", description = "Work-item numeric id." },
            commentId = new { type = "integer", description = "Comment id (from ado_bridge_list_comments)." },
        },
        required = new[] { "organization", "project", "workItemId", "commentId" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var workItemId = arguments.GetProperty("workItemId").GetInt32();
        var commentId = arguments.GetProperty("commentId").GetInt32();

        _logger.LogInformation(
            "ado_bridge_get_comment: WI {Id} comment {CommentId}", workItemId, commentId);

        JsonElement? comment;
        try
        {
            comment = await _ado.GetWorkItemCommentAsync(org, project, workItemId, commentId, ct)
                                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        if (comment is null)
            return new McpToolResult(
                $"Comment {commentId} not found on work item {workItemId}.", IsError: true);

        var text = comment.Value.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
            ? textEl.GetString() ?? string.Empty
            : string.Empty;

        return new McpToolResult(AdoFieldEscaper.Unescape(text));
    }
}
