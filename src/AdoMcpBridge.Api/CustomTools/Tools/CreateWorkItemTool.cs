using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdoMcpBridge.Core.BlobStorage;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class CreateWorkItemTool : ICustomMcpTool
{
    private readonly IBlobSlotStore _blobs;
    private readonly IAdoRestClient _ado;
    private readonly ILogger<CreateWorkItemTool> _logger;

    public CreateWorkItemTool(
        IBlobSlotStore blobs, IAdoRestClient ado, ILogger<CreateWorkItemTool> logger)
    {
        _blobs = blobs;
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_create_work_item";
    public object? Annotations => new { readOnlyHint = false };
    public string Description =>
        "Write operations: Creates an Azure DevOps work item AND populates its long-text fields " +
        "(Description, ReproSteps, AcceptanceCriteria, …) from previously created upload slots in a " +
        "single ADO call — no create-then-patch dance. Scalar fields (System.Title, System.State, " +
        "System.Tags, System.AreaPath, …) are passed as string values. Each long-text field is filled " +
        "from an upload slot (see ado_bridge_create_upload_slot) whose SHA-256 the bridge verifies before " +
        "writing; long-text format DEFAULTS to Markdown (entity-escaped to survive ADO's ingest sanitiser " +
        "and stored via /multilineFieldsFormat) — there is no HTML default. Pass format='html' per field to " +
        "send HTML as-is. If any slot is missing or its hash mismatches, no work item is created. " +
        "This tool does NOT round-trip-verify each field — use ado_bridge_write_field_from_slot when you " +
        "need a verified single-field write. " +
        "Each sha256 must be the lowercase hex SHA-256 of the raw UTF-8 bytes of the uploaded file.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name." },
            project = new { type = "string", description = "ADO project name." },
            workItemType = new { type = "string", description = "Work-item type, e.g. \"Bug\", \"Task\", \"User Story\"." },
            fields = new
            {
                type = "array",
                description = "Scalar fields to set (System.Title, System.State, System.Tags, System.AreaPath, …). Values are strings.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Field reference name, e.g. System.Title." },
                        value = new { type = "string", description = "Field value (string)." },
                    },
                    required = new[] { "name", "value" },
                },
            },
            longTextFields = new
            {
                type = "array",
                description = "Long-text fields to fill from upload slots. format defaults to 'markdown'.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        fieldRefName = new { type = "string", description = "Long-text field reference name, e.g. System.Description." },
                        slotId = new { type = "string", description = "Slot ID returned by ado_bridge_create_upload_slot." },
                        sha256 = new { type = "string", description = "Lowercase hex SHA-256 of the raw UTF-8 bytes of the uploaded content." },
                        format = new { type = "string", @enum = new[] { "markdown", "html" }, description = "Optional — defaults to 'markdown'. 'markdown': entity-escaped and stored as native Markdown. 'html': sent as-is." },
                    },
                    required = new[] { "fieldRefName", "slotId", "sha256" },
                },
            },
        },
        required = new[] { "organization", "project", "workItemType" },
    };

    private sealed record LongTextSpec(string FieldRefName, string SlotId, string Sha256, string? FormatRaw);

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var workItemType = ToolArgs.RequireString(arguments, "workItemType");

        // Parse both arrays first — a malformed entry is a caller error (-32602) and must be
        // rejected before any slot read or ADO call.
        var scalarFields = ParseScalarFields(arguments);
        var longTextSpecs = ParseLongTextFields(arguments);

        _logger.LogInformation(
            "ado_bridge_create_work_item: creating {Type} in {Org}/{Project} scalars={Scalars} longText={LongText}",
            workItemType, org, project, scalarFields.Count, longTextSpecs.Count);

        var ops = new List<object>();
        foreach (var (name, value) in scalarFields)
            ops.Add(new { op = "add", path = $"/fields/{name}", value = (object)value });

        // Read and validate EVERY slot before the POST so a bad slot never leaves a
        // half-populated work item.
        foreach (var spec in longTextSpecs)
        {
            var isMarkdown = spec.FormatRaw is null ||
                             string.Equals(spec.FormatRaw, "markdown", StringComparison.OrdinalIgnoreCase);
            var isHtml = string.Equals(spec.FormatRaw, "html", StringComparison.OrdinalIgnoreCase);
            if (!isMarkdown && !isHtml)
            {
                var received = spec.FormatRaw is null ? "(omitted)" : $"'{spec.FormatRaw}'";
                _logger.LogWarning(
                    "ado_bridge_create_work_item: rejected — field {Field} format={Received}",
                    spec.FieldRefName, received);
                return new McpToolResult(
                    $"format for '{spec.FieldRefName}' must be 'markdown' or 'html' (defaults to " +
                    $"'markdown' when omitted). received={received}", IsError: true);
            }

            byte[] rawBytes;
            try
            {
                rawBytes = await _blobs.ReadSlotAsync(spec.SlotId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read slot {SlotId}", spec.SlotId);
                return new McpToolResult(
                    $"Failed to read upload slot '{spec.SlotId}' for field '{spec.FieldRefName}': {ex.Message}",
                    IsError: true);
            }

            var actualSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
            var expectedSha = spec.Sha256.ToLowerInvariant();
            if (actualSha != expectedSha)
            {
                _logger.LogWarning(
                    "Slot {SlotId} SHA-256 mismatch for field {Field}: expected {Expected} actual {Actual}",
                    spec.SlotId, spec.FieldRefName, expectedSha, actualSha);
                return new McpToolResult(
                    $"SHA-256 mismatch for field '{spec.FieldRefName}'. expected={expectedSha} actual={actualSha}",
                    IsError: true);
            }

            var content = Encoding.UTF8.GetString(rawBytes);
            // Markdown is entity-escaped to survive ADO's ingest sanitiser (WI #95818); HTML is
            // sent as-is. Markdown also declares native storage via /multilineFieldsFormat.
            var writeValue = isMarkdown ? AdoFieldEscaper.Escape(content) : content;
            ops.Add(new { op = "add", path = $"/fields/{spec.FieldRefName}", value = (object)writeValue });
            if (isMarkdown)
                ops.Add(new { op = "add", path = $"/multilineFieldsFormat/{spec.FieldRefName}", value = (object)"Markdown" });
        }

        JsonElement created;
        try
        {
            created = await _ado.CreateWorkItemAsync(org, project, workItemType, ops, ct).ConfigureAwait(false);
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

        var id = created.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idValue)
            ? idValue
            : (int?)null;

        // Best-effort slot cleanup — non-fatal, the lifecycle policy sweeps orphaned blobs.
        foreach (var spec in longTextSpecs)
        {
            try
            {
                await _blobs.DeleteSlotAsync(spec.SlotId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete slot {SlotId} after create", spec.SlotId);
            }
        }

        _logger.LogInformation(
            "ado_bridge_create_work_item: created {Type} id={Id} scalars={Scalars} longText={LongText}",
            workItemType, id, scalarFields.Count, longTextSpecs.Count);

        return new McpToolResult(JsonSerializer.Serialize(new
        {
            status = "CREATED",
            id,
            longTextFieldCount = longTextSpecs.Count,
            scalarFieldCount = scalarFields.Count,
        }));
    }

    private static List<(string Name, string Value)> ParseScalarFields(JsonElement arguments)
    {
        var result = new List<(string, string)>();
        if (GetArrayArg(arguments, "fields") is not { } arr)
            return result;
        foreach (var element in arr.EnumerateArray())
        {
            var name = ToolArgs.RequireString(element, "name");
            var value = ToolArgs.RequireString(element, "value");
            result.Add((name, value));
        }
        return result;
    }

    private static List<LongTextSpec> ParseLongTextFields(JsonElement arguments)
    {
        var result = new List<LongTextSpec>();
        if (GetArrayArg(arguments, "longTextFields") is not { } arr)
            return result;
        foreach (var element in arr.EnumerateArray())
        {
            var fieldRefName = ToolArgs.RequireString(element, "fieldRefName");
            var slotId = ToolArgs.RequireString(element, "slotId");
            var sha256 = ToolArgs.RequireString(element, "sha256");
            // format is optional; anything present-but-invalid is validated later as an IsError
            // result rather than a caller-argument error.
            var formatRaw = element.TryGetProperty("format", out var fmtEl) && fmtEl.ValueKind == JsonValueKind.String
                ? fmtEl.GetString()
                : null;
            result.Add(new LongTextSpec(fieldRefName, slotId, sha256, formatRaw));
        }
        return result;
    }

    // Returns an optional array argument: absent or JSON null → null; present-but-not-an-array →
    // caller error (-32602).
    private static JsonElement? GetArrayArg(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var arr))
            return null;
        if (arr.ValueKind == JsonValueKind.Null)
            return null;
        if (arr.ValueKind != JsonValueKind.Array)
            throw new CallerArgumentException($"'{name}' must be an array.");
        return arr;
    }
}
