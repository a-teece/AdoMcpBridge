using System.Text.Json;
using AdoMcpBridge.Core.BlobStorage;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class CreateUploadSlotTool : ICustomMcpTool
{
    private readonly IBlobSlotStore _blobs;
    private readonly ILogger<CreateUploadSlotTool> _logger;

    public CreateUploadSlotTool(IBlobSlotStore blobs, ILogger<CreateUploadSlotTool> logger)
    {
        _blobs = blobs;
        _logger = logger;
    }

    public string Name => "ado_bridge_create_upload_slot";
    public object? Annotations => new { readOnlyHint = false };
    public string Description =>
        "Write operations: Creates a short-lived pre-signed upload slot for transferring large text " +
        "content to the bridge without routing it through the model. Returns a write-only SAS URL and a slot ID. " +
        "After uploading, call ado_bridge_write_field_from_slot to transfer the content into an " +
        "Azure DevOps work-item field. " +
        "Upload the file with: curl -X PUT -H \"x-ms-blob-type: BlockBlob\" --data-binary @<file> \"$uploadUrl\"";

    public object InputSchema => new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>(),
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        _logger.LogInformation("ado_bridge_create_upload_slot: creating slot");

        UploadSlot slot;
        try
        {
            slot = await _blobs.CreateSlotAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create upload slot");
            return new McpToolResult($"Failed to create upload slot: {ex.Message}", IsError: true);
        }

        var result = JsonSerializer.Serialize(new
        {
            slotId = slot.SlotId,
            uploadUrl = slot.UploadUrl.ToString(),
            expiresAt = slot.ExpiresAt.ToString("O"),
        });

        return new McpToolResult(result);
    }
}
