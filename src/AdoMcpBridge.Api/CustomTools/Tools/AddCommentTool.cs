using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdoMcpBridge.Core.BlobStorage;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class AddCommentTool : ICustomMcpTool
{
    /// <summary>
    /// Inline <c>text</c> above this length is rejected in favour of the upload-slot
    /// path, so a large comment body never has to travel through the model as a
    /// tool-call argument. Mirrors the read-side oversize ceiling.
    /// </summary>
    internal const int InlineCommentCharLimit = 4096;

    private readonly IBlobSlotStore _blobs;
    private readonly IAdoRestClient _ado;
    private readonly ILogger<AddCommentTool> _logger;

    public AddCommentTool(IBlobSlotStore blobs, IAdoRestClient ado, ILogger<AddCommentTool> logger)
    {
        _blobs = blobs;
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_add_comment";
    public object? Annotations => new { readOnlyHint = false };

    public string Description =>
        "Write operations: Posts a comment on an Azure DevOps work item. " +
        "format is REQUIRED and must be explicitly 'markdown' or 'html' — there is no default. " +
        "format=markdown stores the body as native Markdown so ADO renders it; format=html stores it as HTML. " +
        "(A comment posted without an explicit format defaults to HTML in ADO, which shows a Markdown body as " +
        "raw syntax behind a 'convert to markdown' prompt — the reason this tool refuses to guess.) " +
        "Small bodies: pass the body inline via 'text'. " +
        $"Bodies longer than {InlineCommentCharLimit} characters MUST instead be uploaded via " +
        "ado_bridge_create_upload_slot and posted by passing 'slotId' + 'sha256' (the bridge verifies " +
        "the SHA-256 and reads the body from the slot), so a large comment never routes through the model. " +
        "Provide exactly one of 'text' or 'slotId'. " +
        "Returns {\"status\":\"ADDED\",\"commentId\":N,\"charCount\":N,\"format\":\"markdown|html\"}. " +
        "The body is stored verbatim.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            workItemId = new { type = "integer", description = "Work-item numeric id." },
            text = new { type = "string", description = $"Inline comment body. Use only for bodies up to {InlineCommentCharLimit} characters; otherwise use slotId." },
            slotId = new { type = "string", description = "Upload slot id (from ado_bridge_create_upload_slot) holding the comment body. Requires sha256." },
            sha256 = new { type = "string", description = "Lowercase hex SHA-256 of the raw UTF-8 bytes of the uploaded body. Required with slotId." },
            format = new { type = "string", @enum = new[] { "markdown", "html" }, description = "REQUIRED — no default is assumed. 'markdown': body stored as native Markdown (ADO renders it). 'html': body stored as HTML. A comment posted without a format defaults to HTML and shows Markdown as raw syntax." },
        },
        required = new[] { "organization", "project", "workItemId", "format" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        // `format` must be stated explicitly. A JSON Schema `required` array is not enforced by
        // every MCP client, so validate here — and validate first, so a bad request fails before
        // the slot is read or any ADO call is made.
        var hasFormat = arguments.TryGetProperty("format", out var fmtEl);
        var formatValue = hasFormat && fmtEl.ValueKind == JsonValueKind.String ? fmtEl.GetString() : null;
        var isMarkdown = string.Equals(formatValue, "markdown", StringComparison.OrdinalIgnoreCase);
        var isHtml = string.Equals(formatValue, "html", StringComparison.OrdinalIgnoreCase);
        if (!isMarkdown && !isHtml)
        {
            var received = !hasFormat
                ? "(omitted)"
                : fmtEl.ValueKind == JsonValueKind.Null ? "null" : $"'{fmtEl}'";
            _logger.LogWarning("ado_bridge_add_comment: rejected — format={Received}", received);
            return new McpToolResult(
                "format is required and must be explicitly 'markdown' or 'html' — " +
                $"no default is assumed. received={received}", IsError: true);
        }

        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var workItemId = ToolArgs.RequireInt(arguments, "workItemId");

        var text = arguments.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
            ? textEl.GetString()
            : null;
        var slotId = arguments.TryGetProperty("slotId", out var slotEl) && slotEl.ValueKind == JsonValueKind.String
            ? slotEl.GetString()
            : null;

        if (string.IsNullOrEmpty(text) == string.IsNullOrEmpty(slotId))
            return new McpToolResult(
                "Provide exactly one of 'text' (small bodies) or 'slotId' (large bodies).", IsError: true);

        _logger.LogInformation(
            "ado_bridge_add_comment: WI {Id} in {Org}/{Project} via {Source}",
            workItemId, org, project, slotId is null ? "inline" : "slot");

        string body;
        if (slotId is not null)
        {
            var resolved = await ResolveFromSlotAsync(arguments, slotId, ct).ConfigureAwait(false);
            if (resolved.Error is not null) return resolved.Error;
            body = resolved.Body!;
        }
        else
        {
            if (text!.Length > InlineCommentCharLimit)
                return new McpToolResult(
                    $"Inline comment body is {text.Length} characters, over the {InlineCommentCharLimit} limit. " +
                    "Upload it via ado_bridge_create_upload_slot and pass slotId + sha256 instead.",
                    IsError: true);
            body = text;
        }

        JsonElement created;
        try
        {
            created = await _ado.AddWorkItemCommentAsync(org, project, workItemId, body, isMarkdown, ct)
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

        int? commentId = created.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
            ? idEl.GetInt32()
            : null;

        _logger.LogInformation(
            "ado_bridge_add_comment: WI {Id} status=ADDED commentId={CommentId} chars={Chars} format={Format}",
            workItemId, commentId, body.Length, isMarkdown ? "markdown" : "html");

        return new McpToolResult(
            JsonSerializer.Serialize(new
            {
                status = "ADDED",
                commentId,
                charCount = body.Length,
                format = isMarkdown ? "markdown" : "html",
            }));
    }

    private async Task<(string? Body, McpToolResult? Error)> ResolveFromSlotAsync(
        JsonElement arguments, string slotId, CancellationToken ct)
    {
        var expectedSha = arguments.TryGetProperty("sha256", out var shaEl) && shaEl.ValueKind == JsonValueKind.String
            ? shaEl.GetString()?.ToLowerInvariant()
            : null;

        if (string.IsNullOrEmpty(expectedSha))
            return (null, new McpToolResult("'sha256' is required when posting from a slot.", IsError: true));

        byte[] rawBytes;
        try
        {
            rawBytes = await _blobs.ReadSlotAsync(slotId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read slot {SlotId}", slotId);
            return (null, new McpToolResult($"Failed to read upload slot '{slotId}': {ex.Message}", IsError: true));
        }

        var actualSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        if (actualSha != expectedSha)
        {
            _logger.LogWarning(
                "Slot {SlotId} SHA-256 mismatch: expected {Expected} actual {Actual}",
                slotId, expectedSha, actualSha);
            return (null, new McpToolResult(
                $"SHA-256 mismatch. expected={expectedSha} actual={actualSha}", IsError: true));
        }

        // Clean up the slot best-effort — the lifecycle policy sweeps any orphans.
        try
        {
            await _blobs.DeleteSlotAsync(slotId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete slot {SlotId} after read", slotId);
        }

        return (Encoding.UTF8.GetString(rawBytes), null);
    }
}
