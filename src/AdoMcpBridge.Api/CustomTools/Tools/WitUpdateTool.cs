using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class WitUpdateTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly ILogger<WitUpdateTool> _logger;

    public WitUpdateTool(IAdoRestClient ado, ILogger<WitUpdateTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_update";
    public object? Annotations => new { readOnlyHint = false };
    public string Description =>
        "Write operations: Updates scalar fields of one Azure DevOps work item in a single PATCH — " +
        "title, state, tags, assignment, area/iteration path, priority and other short fields. Each " +
        "entry in 'fields' is {name, value, op}: op 'set' (default) writes the value, op 'clear' " +
        "removes the field's value. Numbers and booleans are sent as strings. Note System.Tags is " +
        "the whole semicolon-separated tag list: 'set' replaces it. Long-text fields (" +
        string.Join(", ", BasicToolGuardrails.LongTextFieldRefNames) +
        ") are rejected — write them with 'ado_bridge_create_upload_slot' + " +
        "'ado_bridge_write_field_from_slot'. System.Parent is rejected — use 'ado_bridge_wit_link'. " +
        "Returns {status, id, rev, changedFields}. For several work items use 'ado_bridge_wit_update_batch'.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name." },
            project = new { type = "string", description = "ADO project name." },
            id = new { type = "integer", description = "Work item ID." },
            fields = FieldsSchema,
        },
        required = new[] { "organization", "project", "id", "fields" },
    };

    /// <summary>The <c>fields</c> array schema, shared with <see cref="WitUpdateBatchTool"/>.</summary>
    internal static object FieldsSchema => new
    {
        type = "array",
        minItems = 1,
        description = "Fields to change. Use field reference names, e.g. System.State.",
        items = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "Field reference name, e.g. System.Title." },
                value = new
                {
                    type = new[] { "string", "number", "boolean" },
                    description = "New value (required for op 'set'; ignored for 'clear'). Numbers and booleans are stringified.",
                },
                op = new
                {
                    type = "string",
                    @enum = new[] { "set", "clear" },
                    description = "'set' (default) writes the value; 'clear' removes it.",
                },
            },
            required = new[] { "name" },
        },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var id = ToolArgs.RequireInt(arguments, "id");
        var (ops, names) = BuildPatch(arguments);

        _logger.LogInformation(
            "ado_bridge_wit_update: updating WI {Id} in {Org}/{Project} fields={Count}",
            id, org, project, names.Count);

        JsonElement updated;
        try
        {
            updated = await _ado.UpdateWorkItemAsync(org, project, id, ops, ct).ConfigureAwait(false);
        }
        catch (AdoRestException ex)
        {
            return new McpToolResult($"Azure DevOps returned HTTP {ex.StatusCode}: {ex.Message}", IsError: true);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed (transport): {ex.Message}", IsError: true);
        }

        return new McpToolResult(JsonSerializer.Serialize(new
        {
            status = "UPDATED",
            id,
            rev = ReadRev(updated),
            changedFields = names,
        }));
    }

    /// <summary>
    /// Validates <paramref name="container"/>'s <c>fields</c> array and compiles it to JSON-Patch
    /// ops: <c>set</c> → <c>add</c> (with the stringified value), <c>clear</c> → <c>remove</c>.
    /// Every entry is checked — including <see cref="WorkItemFieldGuard"/> — before anything is
    /// returned, so a bad entry means no write at all.
    /// </summary>
    /// <exception cref="CallerArgumentException">The array or one of its entries is invalid.</exception>
    internal static (List<object> Ops, List<string> Names) BuildPatch(JsonElement container)
    {
        if (container.ValueKind != JsonValueKind.Object ||
            !container.TryGetProperty("fields", out var fields) ||
            fields.ValueKind != JsonValueKind.Array ||
            fields.GetArrayLength() == 0)
        {
            throw new CallerArgumentException("'fields' is required and must be a non-empty array.");
        }

        var ops = new List<object>();
        var names = new List<string>();
        foreach (var entry in fields.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new CallerArgumentException("Each 'fields' entry must be an object {name, value, op}.");

            var name = ToolArgs.RequireString(entry, "name").Trim();
            if (name.Contains('/'))
                throw new CallerArgumentException($"Field name '{name}' must not contain '/'.");
            WorkItemFieldGuard.ThrowIfForbidden(name);

            var path = $"/fields/{name}";
            if (IsClear(entry, name))
                ops.Add(new { op = "remove", path });
            else
                ops.Add(new { op = "add", path, value = (object)ReadValue(entry, name) });
            names.Add(name);
        }

        return (ops, names);
    }

    internal static int? ReadRev(JsonElement workItem)
        => workItem.ValueKind == JsonValueKind.Object &&
           workItem.TryGetProperty("rev", out var rev) &&
           rev.TryGetInt32(out var value)
            ? value
            : null;

    private static bool IsClear(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty("op", out var op) || op.ValueKind == JsonValueKind.Null)
            return false;

        var text = op.ValueKind == JsonValueKind.String ? op.GetString() : null;
        return text switch
        {
            "set" => false,
            "clear" => true,
            _ => throw new CallerArgumentException($"'op' for field '{name}' must be 'set' or 'clear'."),
        };
    }

    private static string ReadValue(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null)
            throw new CallerArgumentException($"'value' is required for op 'set' on field '{name}'.");

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new CallerArgumentException(
                $"'value' for field '{name}' must be a string, number or boolean."),
        };
    }
}
