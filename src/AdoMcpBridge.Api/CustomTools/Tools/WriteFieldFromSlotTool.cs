using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdoMcpBridge.Core.BlobStorage;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class WriteFieldFromSlotTool : ICustomMcpTool
{
    private readonly IBlobSlotStore _blobs;
    private readonly IAdoRestClient _ado;
    private readonly ILogger<WriteFieldFromSlotTool> _logger;

    public WriteFieldFromSlotTool(
        IBlobSlotStore blobs, IAdoRestClient ado, ILogger<WriteFieldFromSlotTool> logger)
    {
        _blobs = blobs;
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_write_field_from_slot";
    public string Description =>
        "Transfers content from a previously created upload slot into an Azure DevOps work-item " +
        "long-text field. The bridge verifies the SHA-256 hash of the uploaded content, " +
        "applies the ADO entity-escaping required to survive the ADO ingest sanitiser (WI #95818), " +
        "writes the field, re-fetches it, and verifies the round-trip. " +
        "Returns {\"status\":\"MATCH\",\"charCount\":N} on success. " +
        "The sha256 must be the lowercase hex SHA-256 of the raw UTF-8 bytes of the markdown file.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            slotId = new { type = "string", description = "Slot ID returned by ado_bridge_create_upload_slot." },
            organization = new { type = "string", description = "ADO organisation name." },
            project = new { type = "string", description = "ADO project name." },
            workItemId = new { type = "integer", description = "Work-item numeric id." },
            fieldRefName = new { type = "string", description = "Field reference name (e.g. System.Description)." },
            sha256 = new { type = "string", description = "Lowercase hex SHA-256 of the uploaded markdown bytes." },
        },
        required = new[] { "slotId", "organization", "project", "workItemId", "fieldRefName", "sha256" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var slotId = arguments.GetProperty("slotId").GetString()!;
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var workItemId = arguments.GetProperty("workItemId").GetInt32();
        var fieldRef = arguments.GetProperty("fieldRefName").GetString()!;
        var expectedSha = arguments.GetProperty("sha256").GetString()!.ToLowerInvariant();

        _logger.LogInformation(
            "ado_bridge_write_field_from_slot: WI {Id} field {Field} slot {Slot}",
            workItemId, fieldRef, slotId);

        // 1. Download the uploaded content.
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

        // 2. Verify SHA-256 (over raw bytes, before any transform).
        var actualSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        if (actualSha != expectedSha)
        {
            _logger.LogWarning(
                "Slot {SlotId} SHA-256 mismatch: expected {Expected} actual {Actual}",
                slotId, expectedSha, actualSha);
            return new McpToolResult(
                $"SHA-256 mismatch. expected={expectedSha} actual={actualSha}", IsError: true);
        }

        var markdown = Encoding.UTF8.GetString(rawBytes);
        var escapedValue = AdoFieldEscaper.Escape(markdown);

        // 3. Write the escaped content to the ADO field.
        try
        {
            await _ado.PatchFieldAsync(org, project, workItemId, fieldRef, escapedValue, ct)
                      .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO PATCH failed: {ex.Message}", IsError: true);
        }

        // 4. Re-fetch and verify round-trip.
        string? storedValue;
        try
        {
            storedValue = await _ado.GetFieldAsync(org, project, workItemId, fieldRef, ct)
                                    .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult(
                $"Write succeeded but re-fetch failed (cannot verify): {ex.Message}", IsError: true);
        }

        // 5. Clean up the slot regardless of verify outcome.
        try
        {
            await _blobs.DeleteSlotAsync(slotId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Non-fatal — the lifecycle policy will clean up orphaned blobs.
            _logger.LogWarning(ex, "Failed to delete slot {SlotId} after write", slotId);
        }

        var (isMatch, charCount) = AdoFieldEscaper.Verify(markdown, storedValue ?? string.Empty);

        _logger.LogInformation(
            "ado_bridge_write_field_from_slot: WI {Id} field {Field} status={Status} chars={Chars}",
            workItemId, fieldRef, isMatch ? "MATCH" : "MISMATCH", charCount);

        var response = JsonSerializer.Serialize(new
        {
            status = isMatch ? "MATCH" : "MISMATCH",
            charCount,
        });

        return new McpToolResult(response, IsError: !isMatch);
    }
}
