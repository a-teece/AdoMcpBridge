using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class ListCommentsTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly ILogger<ListCommentsTool> _logger;

    public ListCommentsTool(IAdoRestClient ado, ILogger<ListCommentsTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_list_comments";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Lists the comments on an Azure DevOps work item as compact metadata only " +
        "(id, author, dates, character count, and a short preview) — the full comment bodies are NOT " +
        "returned, so a long thread cannot blow the context budget. Returns {\"count\":N,\"value\":[...]}. " +
        "Use ado_bridge_get_comment with a comment id to read one full comment body.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            workItemId = new { type = "integer", description = "Work-item numeric id." },
        },
        required = new[] { "organization", "project", "workItemId" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var workItemId = arguments.GetProperty("workItemId").GetInt32();

        _logger.LogInformation("ado_bridge_list_comments: WI {Id} in {Org}/{Project}", workItemId, org, project);

        IReadOnlyList<JsonElement> comments;
        try
        {
            comments = await _ado.GetWorkItemCommentsAsync(org, project, workItemId, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", comments.Count);
            writer.WritePropertyName("value");
            writer.WriteStartArray();
            foreach (var comment in comments)
                CommentProjection.WriteCommentMetadata(writer, comment);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return new McpToolResult(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
