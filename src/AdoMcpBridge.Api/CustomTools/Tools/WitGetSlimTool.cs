using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class WitGetSlimTool : ICustomMcpTool
{
    private readonly IAdoRestClient _ado;
    private readonly IWorkItemFieldTypeCache _fieldTypes;
    private readonly ILogger<WitGetSlimTool> _logger;

    public WitGetSlimTool(
        IAdoRestClient ado, IWorkItemFieldTypeCache fieldTypes, ILogger<WitGetSlimTool> logger)
    {
        _ado = ado;
        _fieldTypes = fieldTypes;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_get";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Gets an Azure DevOps work item with all long-text fields (HTML descriptions, " +
        "repro steps, acceptance criteria, etc.) replaced by compact stub markers showing only the character count. " +
        "Use ado_bridge_download_field to fetch the full content of any stubbed field. " +
        "All other fields (title, state, type, priority, dates, tags, relations, etc.) are returned in full.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            id = new { type = "integer", description = "Work-item numeric id." },
        },
        required = new[] { "organization", "project", "id" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var id = arguments.GetProperty("id").GetInt32();

        _logger.LogInformation("ado_bridge_wit_get: WI {Id} in {Org}/{Project}", id, org, project);

        JsonElement? workItem;
        IReadOnlySet<string> longTextFields;
        try
        {
            var witTask = _ado.GetWorkItemAsync(org, project, id, ct);
            var typesTask = _fieldTypes.GetLongTextFieldRefNamesAsync(org, ct);
            await Task.WhenAll(witTask, typesTask).ConfigureAwait(false);
            workItem = await witTask;
            longTextFields = await typesTask;
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        if (workItem is null)
            return new McpToolResult($"Work item {id} not found.", IsError: true);

        return new McpToolResult(BuildSlimJson(workItem.Value, longTextFields));
    }

    internal static string BuildSlimJson(JsonElement workItem, IReadOnlySet<string> longTextFields)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            WriteSlimWorkItem(writer, workItem, longTextFields);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    internal static void WriteSlimWorkItem(
        Utf8JsonWriter writer, JsonElement workItem, IReadOnlySet<string> longTextFields)
    {
        writer.WriteStartObject();
        foreach (var prop in workItem.EnumerateObject())
        {
            if (prop.NameEquals("fields"))
            {
                writer.WritePropertyName("fields");
                WriteSlimFields(writer, prop.Value, longTextFields);
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteSlimFields(
        Utf8JsonWriter writer, JsonElement fields, IReadOnlySet<string> longTextFields)
    {
        writer.WriteStartObject();
        foreach (var field in fields.EnumerateObject())
        {
            if (longTextFields.Contains(field.Name) &&
                field.Value.ValueKind == JsonValueKind.String &&
                field.Value.GetString() is { Length: > 0 } value)
            {
                writer.WritePropertyName(field.Name);
                writer.WriteStartObject();
                writer.WriteNumber("charCount", value.Length);
                writer.WriteString("note", "Field contains long text. Use ado_bridge_download_field to read it.");
                writer.WriteEndObject();
            }
            else
            {
                field.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }
}
