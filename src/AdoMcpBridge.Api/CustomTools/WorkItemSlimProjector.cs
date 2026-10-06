using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// The slim work-item projection shared by every read tool that returns work items: each
/// top-level property is passed through, except <c>fields</c>, where long-text fields (and
/// any string over <see cref="OversizeFieldCharCeiling"/>) are replaced by a
/// <c>{ charCount, note }</c> stub pointing at the download tools.
/// </summary>
internal static class WorkItemSlimProjector
{
    /// <summary>
    /// Defensive ceiling: any string field longer than this is stubbed even when it
    /// is not a known long-text field, so a new/custom field type can never silently
    /// reintroduce an oversized read that blows the model's context budget. Tune here.
    /// </summary>
    internal const int OversizeFieldCharCeiling = 4096;

    /// <summary>
    /// Writes <paramref name="workItem"/> (a work item as returned by the ADO REST API) to
    /// <paramref name="writer"/> as one slim JSON object, stubbing every non-empty string field
    /// named in <paramref name="longTextFields"/> or longer than <see cref="OversizeFieldCharCeiling"/>.
    /// </summary>
    public static void Write(Utf8JsonWriter writer, JsonElement workItem, IReadOnlySet<string> longTextFields)
    {
        writer.WriteStartObject();
        foreach (var prop in workItem.EnumerateObject())
        {
            if (prop.NameEquals("fields"))
            {
                writer.WritePropertyName("fields");
                WriteFields(writer, prop.Value, longTextFields);
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteFields(
        Utf8JsonWriter writer, JsonElement fields, IReadOnlySet<string> longTextFields)
    {
        writer.WriteStartObject();
        foreach (var field in fields.EnumerateObject())
        {
            if (field.Value.ValueKind == JsonValueKind.String &&
                field.Value.GetString() is { Length: > 0 } value &&
                (longTextFields.Contains(field.Name) || value.Length > OversizeFieldCharCeiling))
            {
                writer.WritePropertyName(field.Name);
                writer.WriteStartObject();
                writer.WriteNumber("charCount", value.Length);
                writer.WriteString("note", "Field contains long text. Use ado_bridge_download_field to read it inline, " +
                    "or ado_bridge_download_field_as_file to save it to a file without loading it into context.");
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
