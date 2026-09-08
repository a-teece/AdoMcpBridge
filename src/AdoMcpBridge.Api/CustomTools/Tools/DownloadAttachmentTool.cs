using System.Security.Cryptography;
using System.Text.Json;
using AdoMcpBridge.Core.BlobStorage;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Native replacement for the upstream <c>wit_work_item_attachment</c> tool, which returns
/// the whole attachment as base64 inline — unworkable for multi-MB files routed through the
/// model. This fetches the bytes server-side, stakes them in a blob, and returns a short-lived
/// read-only SAS <c>downloadUrl</c> the client pulls directly. Mirrors the long-text
/// download/slot pattern, but for binary content.
/// </summary>
internal sealed class DownloadAttachmentTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly IBlobSlotStore _blobs;
    private readonly ILogger<DownloadAttachmentTool> _logger;

    public DownloadAttachmentTool(
        IAdoRestClient ado, IBlobSlotStore blobs, ILogger<DownloadAttachmentTool> logger)
    {
        _ado = ado;
        _blobs = blobs;
        _logger = logger;
    }

    public string Name => "ado_bridge_download_attachment";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Downloads an Azure DevOps work-item attachment WITHOUT routing its bytes " +
        "through the model. The bridge fetches the attachment server-side and returns a short-lived, " +
        "read-only SAS 'downloadUrl' plus fileName, sizeBytes, contentType and a sha256 for integrity. " +
        "Provide the attachment 'id' (a GUID) or its 'url' (the AttachedFile relation url from " +
        "ado_bridge_wit_get); pass exactly one. " +
        "Download the file with: curl -o <fileName> \"$downloadUrl\"";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            id = new { type = "string", description = "Attachment id (GUID). Provide this or 'url', not both." },
            url = new { type = "string", description = "Attachment url (the AttachedFile relation url). Provide this or 'id', not both." },
            fileName = new { type = "string", description = "Optional original file name; improves the served download name." },
        },
        required = new[] { "organization", "project" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;

        var idArg = arguments.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;
        var urlArg = arguments.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String
            ? urlEl.GetString()
            : null;
        var fileName = arguments.TryGetProperty("fileName", out var fnEl) && fnEl.ValueKind == JsonValueKind.String
            ? fnEl.GetString()
            : null;

        if (string.IsNullOrEmpty(idArg) == string.IsNullOrEmpty(urlArg))
            return new McpToolResult("Provide exactly one of 'id' (attachment GUID) or 'url'.", IsError: true);

        // Mirror the exactly-one check above (which treats empty as absent): an empty id
        // falls through to resolving from the url rather than failing worded for 'id'.
        var attachmentId = !string.IsNullOrEmpty(idArg) ? idArg : ExtractAttachmentId(urlArg!);
        if (attachmentId is null || !Guid.TryParse(attachmentId, out _))
            return new McpToolResult(
                $"Could not resolve a valid attachment GUID from {(idArg is not null ? "id" : "url")}.", IsError: true);

        _logger.LogInformation(
            "ado_bridge_download_attachment: {AttachmentId} in {Org}/{Project}", attachmentId, org, project);

        AdoAttachmentContent attachment;
        try
        {
            attachment = await _ado.DownloadAttachmentAsync(org, project, attachmentId, fileName, ct)
                                   .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO attachment download failed: {ex.Message}", IsError: true);
        }

        DownloadSlot slot;
        try
        {
            slot = await _blobs.CreateDownloadSlotAsync(attachment.Content, attachment.ContentType, ct)
                               .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stage attachment {AttachmentId} into a download slot", attachmentId);
            return new McpToolResult($"Failed to create download slot: {ex.Message}", IsError: true);
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(attachment.Content)).ToLowerInvariant();

        _logger.LogInformation(
            "ado_bridge_download_attachment: {AttachmentId} staged slot={Slot} bytes={Bytes}",
            attachmentId, slot.SlotId, attachment.Content.Length);

        return new McpToolResult(JsonSerializer.Serialize(new
        {
            downloadUrl = slot.DownloadUrl.ToString(),
            fileName,
            sizeBytes = attachment.Content.Length,
            contentType = attachment.ContentType,
            sha256,
            expiresAt = slot.ExpiresAt.ToString("O"),
        }));
    }

    /// <summary>
    /// Returns the last non-empty path segment of an attachment url — the GUID — or
    /// <see langword="null"/> if the string is not an absolute uri.
    /// </summary>
    internal static string? ExtractAttachmentId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var segment = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrEmpty(segment) ? null : segment;
    }
}
