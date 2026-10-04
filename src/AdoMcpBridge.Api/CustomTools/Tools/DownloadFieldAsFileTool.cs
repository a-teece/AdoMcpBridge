using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdoMcpBridge.Core.BlobStorage;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// File-staged variant of <see cref="DownloadFieldTool"/>. The inline tool returns the field's
/// markdown in the tool result, so it lands in the model's context. This one fetches and
/// unescapes the field server-side, stages it in a blob, and returns a short-lived read-only SAS
/// <c>downloadUrl</c> so the client can save it to disk (and grep it) without the content ever
/// passing through the model. Mirrors <see cref="DownloadAttachmentTool"/>.
/// </summary>
internal sealed class DownloadFieldAsFileTool : ICustomMcpTool
{
    private const string MarkdownContentType = "text/markdown; charset=utf-8";

    private readonly IAdoRestClient _ado;
    private readonly IBlobSlotStore _blobs;
    private readonly ILogger<DownloadFieldAsFileTool> _logger;

    public DownloadFieldAsFileTool(
        IAdoRestClient ado, IBlobSlotStore blobs, ILogger<DownloadFieldAsFileTool> logger)
    {
        _ado = ado;
        _blobs = blobs;
        _logger = logger;
    }

    public string Name => "ado_bridge_download_field_as_file";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Downloads a large Azure DevOps work-item long-text field (e.g. " +
        "System.Description or Custom.ImplementationPlan) as a markdown file WITHOUT routing its " +
        "content through the model. The bridge fetches the field server-side, reverses ADO " +
        "entity-encoding, and returns a short-lived, read-only SAS 'downloadUrl' plus fileName, " +
        "sizeBytes, contentType and a sha256 for integrity. Prefer this over " +
        "ado_bridge_download_field when the field is large or you want to grep/search it. " +
        "Download the file with: curl -o <fileName> \"$downloadUrl\"";

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
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var workItemId = ToolArgs.RequireInt(arguments, "workItemId");
        var fieldRef = ToolArgs.RequireString(arguments, "fieldRefName");

        _logger.LogInformation(
            "ado_bridge_download_field_as_file: WI {Id} field {Field}", workItemId, fieldRef);

        string? raw;
        try
        {
            raw = await _ado.GetFieldAsync(org, project, workItemId, fieldRef, ct).ConfigureAwait(false);
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

        if (raw is null)
            return new McpToolResult($"Field '{fieldRef}' not found on work item {workItemId}.", IsError: true);

        var bytes = Encoding.UTF8.GetBytes(AdoFieldEscaper.Unescape(raw));

        DownloadSlot slot;
        try
        {
            slot = await _blobs.CreateDownloadSlotAsync(bytes, MarkdownContentType, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to stage field {Field} of WI {Id} into a download slot", fieldRef, workItemId);
            return new McpToolResult($"Failed to create download slot: {ex.Message}", IsError: true);
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        _logger.LogInformation(
            "ado_bridge_download_field_as_file: WI {Id} field {Field} staged slot={Slot} bytes={Bytes}",
            workItemId, fieldRef, slot.SlotId, bytes.Length);

        return new McpToolResult(JsonSerializer.Serialize(new
        {
            downloadUrl = slot.DownloadUrl.ToString(),
            fileName = $"wi-{workItemId}-{fieldRef}.md",
            sizeBytes = bytes.Length,
            contentType = MarkdownContentType,
            sha256,
            expiresAt = slot.ExpiresAt.ToString("O"),
        }));
    }
}
