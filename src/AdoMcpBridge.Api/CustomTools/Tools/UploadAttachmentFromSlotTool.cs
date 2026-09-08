using System.Security.Cryptography;
using System.Text.Json;
using AdoMcpBridge.Core.BlobStorage;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Write counterpart to <see cref="DownloadAttachmentTool"/>: uploads a work-item attachment
/// without routing its bytes through the model. The client stages the file in an upload slot
/// (<c>ado_bridge_create_upload_slot</c>); this tool reads it back server-side, verifies the
/// SHA-256, POSTs it to the Azure DevOps attachment store and — when a work item is given —
/// links it as an <c>AttachedFile</c> relation. Mirrors the long-text write/slot pattern
/// (<see cref="WriteFieldFromSlotTool"/>), but for binary content.
/// </summary>
internal sealed class UploadAttachmentFromSlotTool : ICustomMcpTool
{
    private readonly IBlobSlotStore _blobs;
    private readonly IAdoRestClient _ado;
    private readonly ILogger<UploadAttachmentFromSlotTool> _logger;

    public UploadAttachmentFromSlotTool(
        IBlobSlotStore blobs, IAdoRestClient ado, ILogger<UploadAttachmentFromSlotTool> logger)
    {
        _blobs = blobs;
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_upload_attachment_from_slot";
    public object? Annotations => new { readOnlyHint = false };

    public string Description =>
        "Write operations: Uploads a work-item attachment WITHOUT routing its bytes through the model. " +
        "First call ado_bridge_create_upload_slot and PUT the file to the returned slot URL; then call " +
        "this with slotId + fileName + sha256. The bridge verifies the SHA-256, uploads the bytes to the " +
        "Azure DevOps attachment store, and — when 'workItemId' is supplied — links the attachment to that " +
        "work item as an AttachedFile relation (with an optional 'comment'). Omit workItemId only if you " +
        "will reference the returned attachmentUrl yourself (e.g. embed it in a field); an unlinked " +
        "attachment is otherwise orphaned. Returns " +
        "{\"status\":\"LINKED\"|\"UPLOADED\",\"attachmentId\":\"...\",\"attachmentUrl\":\"...\",\"fileName\":\"...\",\"sizeBytes\":N,\"linkedToWorkItem\":N|null}. " +
        "The sha256 must be the lowercase hex SHA-256 of the raw file bytes.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            slotId = new { type = "string", description = "Slot ID returned by ado_bridge_create_upload_slot (holds the uploaded file)." },
            organization = new { type = "string", description = "ADO organisation name." },
            project = new { type = "string", description = "ADO project name." },
            fileName = new { type = "string", description = "Attachment file name (stored as the attachment's name)." },
            sha256 = new { type = "string", description = "Lowercase hex SHA-256 of the raw bytes of the uploaded file." },
            workItemId = new { type = "integer", description = "Optional. When set, the attachment is linked to this work item as an AttachedFile relation." },
            comment = new { type = "string", description = "Optional comment on the attachment link (only used together with workItemId)." },
        },
        required = new[] { "slotId", "organization", "project", "fileName", "sha256" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var slotId = arguments.GetProperty("slotId").GetString()!;
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var fileName = arguments.GetProperty("fileName").GetString()!;
        var expectedSha = arguments.GetProperty("sha256").GetString()!.ToLowerInvariant();

        int? workItemId = arguments.TryGetProperty("workItemId", out var widEl) && widEl.ValueKind == JsonValueKind.Number
            ? widEl.GetInt32()
            : null;
        var comment = arguments.TryGetProperty("comment", out var cEl) && cEl.ValueKind == JsonValueKind.String
            ? cEl.GetString()
            : null;

        _logger.LogInformation(
            "ado_bridge_upload_attachment_from_slot: slot {Slot} file {File} in {Org}/{Project} link={Link}",
            slotId, fileName, org, project, (object?)workItemId ?? "none");

        // 1. Read the uploaded bytes.
        byte[] rawBytes;
        try
        {
            rawBytes = await _blobs.ReadSlotAsync(slotId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read slot {SlotId}", slotId);
            return new McpToolResult($"Failed to read upload slot '{slotId}': {ex.Message}", IsError: true);
        }

        // 2. Verify SHA-256 over the raw bytes.
        var actualSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        if (actualSha != expectedSha)
        {
            _logger.LogWarning(
                "Slot {SlotId} SHA-256 mismatch: expected {Expected} actual {Actual}",
                slotId, expectedSha, actualSha);
            return new McpToolResult(
                $"SHA-256 mismatch. expected={expectedSha} actual={actualSha}", IsError: true);
        }

        // 3. Upload the bytes to the ADO attachment store.
        AdoAttachmentRef attachment;
        try
        {
            attachment = await _ado.CreateAttachmentAsync(org, project, fileName, rawBytes, ct)
                                   .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO attachment upload failed: {ex.Message}", IsError: true);
        }

        // 4. The bytes now live in ADO — the slot is redundant. Clean it up best-effort
        // (the lifecycle policy sweeps any orphan) before the fallible link step.
        try
        {
            await _blobs.DeleteSlotAsync(slotId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete slot {SlotId} after upload", slotId);
        }

        // 5. Optionally link the attachment to a work item.
        if (workItemId is int wid)
        {
            try
            {
                await _ado.AddWorkItemAttachmentAsync(org, project, wid, attachment.Url, comment, ct)
                          .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                // The file uploaded successfully; only the work-item link failed (e.g. a bad
                // workItemId or missing edit permission). The attachment now lives in the store
                // under the returned url — re-running this tool would upload a duplicate, so the
                // caller should fix the cause and reference attachmentUrl rather than retry blindly.
                _logger.LogWarning(ex, "Attachment {Id} uploaded but linking to WI {Wid} failed", attachment.Id, wid);
                return new McpToolResult(
                    JsonSerializer.Serialize(new
                    {
                        status = "UPLOADED_LINK_FAILED",
                        attachmentId = attachment.Id,
                        attachmentUrl = attachment.Url,
                        fileName,
                        sizeBytes = rawBytes.Length,
                        error = ex.Message,
                        note = "The file uploaded but linking it to the work item failed. " +
                               "Do not re-run this tool (it would create a duplicate attachment) — " +
                               "fix the workItemId/permission cause; the file is available at attachmentUrl.",
                    }),
                    IsError: true);
            }
        }

        var linked = workItemId is not null;
        _logger.LogInformation(
            "ado_bridge_upload_attachment_from_slot: attachment {Id} status={Status} bytes={Bytes}",
            attachment.Id, linked ? "LINKED" : "UPLOADED", rawBytes.Length);

        return new McpToolResult(JsonSerializer.Serialize(new
        {
            status = linked ? "LINKED" : "UPLOADED",
            attachmentId = attachment.Id,
            attachmentUrl = attachment.Url,
            fileName,
            sizeBytes = rawBytes.Length,
            linkedToWorkItem = workItemId,
        }));
    }
}
