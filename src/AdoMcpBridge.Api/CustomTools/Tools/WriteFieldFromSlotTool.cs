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
    public object? Annotations => new { readOnlyHint = false };
    public string Description =>
        "Write operations: Transfers content from a previously created upload slot into an Azure DevOps " +
        "work-item long-text field. The bridge verifies the SHA-256 hash, writes the field, and re-fetches it. " +
        "format is REQUIRED and must be explicitly 'html' or 'markdown' — there is no default. " +
        "format=html: sends HTML as-is; returns {\"status\":\"WRITTEN\",\"charCount\":N}. " +
        "format=markdown: entity-escapes content to survive ADO's ingest sanitiser (WI #95818, strips bare " +
        "<tag> sequences even in markdown mode), declares native Markdown storage via /multilineFieldsFormat, " +
        "verifies the round-trip and returns {\"status\":\"MATCH\",\"charCount\":N}. " +
        "WARNING: once a field is set to Markdown it cannot be reverted to HTML. " +
        "The sha256 must be the lowercase hex SHA-256 of the raw UTF-8 bytes of the uploaded file.";

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
            sha256 = new { type = "string", description = "Lowercase hex SHA-256 of the raw UTF-8 bytes of the uploaded content." },
            format = new { type = "string", @enum = new[] { "html", "markdown" }, description = "REQUIRED — no default is assumed. 'html': HTML sent as-is. 'markdown': entity-escaped (ADO sanitiser strips bare <tag> even in markdown mode) then written with native Markdown storage declared — WARNING: irreversible per field." },
        },
        required = new[] { "slotId", "organization", "project", "workItemId", "fieldRefName", "sha256", "format" },
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
            _logger.LogWarning(
                "ado_bridge_write_field_from_slot: rejected — format={Received}", received);
            return new McpToolResult(
                "format is required and must be explicitly 'html' or 'markdown' — " +
                $"no default is assumed. received={received}", IsError: true);
        }

        var slotId = arguments.GetProperty("slotId").GetString()!;
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var workItemId = arguments.GetProperty("workItemId").GetInt32();
        var fieldRef = arguments.GetProperty("fieldRefName").GetString()!;
        var expectedSha = arguments.GetProperty("sha256").GetString()!.ToLowerInvariant();

        _logger.LogInformation(
            "ado_bridge_write_field_from_slot: WI {Id} field {Field} slot {Slot} format={Format}",
            workItemId, fieldRef, slotId, isMarkdown ? "markdown" : "html");

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

        var content = Encoding.UTF8.GetString(rawBytes);
        // Entity-escape markdown to survive ADO's ingest sanitiser, which strips bare <tag> sequences
        // even in native markdown-mode fields (same bug as WI #95818 on HTML fields).
        // HTML is sent as-is — the caller is responsible for valid HTML.
        var writeValue = isMarkdown ? AdoFieldEscaper.Escape(content) : content;

        // 3. Write the content to the ADO field.
        // For markdown: declare native Markdown storage via /multilineFieldsFormat (irreversible).
        // For html: no format declaration needed — HTML is ADO's default.
        try
        {
            await _ado.PatchFieldAsync(
                    org, project, workItemId, fieldRef, writeValue,
                    fieldFormat: isMarkdown ? "Markdown" : null, ct)
                  .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO PATCH failed: {ex.Message}", IsError: true);
        }

        // 4. Re-fetch to confirm the write succeeded.
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

        if (!isMarkdown)
        {
            var charCount = AdoFieldEscaper.NormalizeTrailingNewlines(content).Length;
            _logger.LogInformation(
                "ado_bridge_write_field_from_slot: WI {Id} field {Field} status=WRITTEN chars={Chars}",
                workItemId, fieldRef, charCount);
            return new McpToolResult(JsonSerializer.Serialize(new { status = "WRITTEN", charCount }));
        }

        // Markdown round-trip: we entity-escaped before writing, so Verify applies Unescape to
        // reverse both our escaping and ADO's own independent quote encoding before comparing.
        var (isMatch, mdCharCount) = AdoFieldEscaper.Verify(content, storedValue ?? string.Empty);

        _logger.LogInformation(
            "ado_bridge_write_field_from_slot: WI {Id} field {Field} status={Status} chars={Chars}",
            workItemId, fieldRef, isMatch ? "MATCH" : "MISMATCH", mdCharCount);

        return new McpToolResult(
            JsonSerializer.Serialize(new { status = isMatch ? "MATCH" : "MISMATCH", charCount = mdCharCount }),
            IsError: !isMatch);
    }
}
