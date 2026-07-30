using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Pure projection of an ADO work-item comment into the compact metadata shape
/// returned by <c>ado_bridge_list_comments</c> — id, author, dates, character count
/// and a short preview, but never the full body. Kept free of IO so it can be
/// unit-tested directly (like <see cref="ApprovalProjection"/>).
/// </summary>
internal static class CommentProjection
{
    /// <summary>Max characters of preview text emitted per comment in a listing.</summary>
    internal const int PreviewCharLimit = 120;

    /// <summary>Writes the compact metadata projection of a single comment to an open writer.</summary>
    internal static void WriteCommentMetadata(Utf8JsonWriter writer, JsonElement comment)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("id");
        if (comment.TryGetProperty("id", out var id))
            id.WriteTo(writer);
        else
            writer.WriteNullValue();

        writer.WriteString("author", GetNestedString(comment, "createdBy", "displayName"));

        if (comment.TryGetProperty("createdDate", out var created))
            writer.WriteString("createdDate", created.GetString());
        if (comment.TryGetProperty("modifiedDate", out var modified) &&
            modified.ValueKind != JsonValueKind.Null)
            writer.WriteString("modifiedDate", modified.GetString());

        var text = comment.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
            ? textEl.GetString() ?? string.Empty
            : string.Empty;

        writer.WriteNumber("charCount", text.Length);
        writer.WriteString("preview", BuildPreview(text));
        writer.WriteString(
            "note", "Metadata only. Use ado_bridge_get_comment with this id to read the full comment.");

        writer.WriteEndObject();
    }

    /// <summary>
    /// First non-empty line of the comment, entity-decoded and truncated. Best-effort
    /// preview only — callers fetch the full body via <c>ado_bridge_get_comment</c>.
    /// </summary>
    internal static string BuildPreview(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var decoded = AdoFieldEscaper.Unescape(text);
        var firstLine = decoded
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? string.Empty;

        return firstLine.Length <= PreviewCharLimit
            ? firstLine
            : firstLine[..PreviewCharLimit] + "…";
    }

    private static string? GetNestedString(JsonElement el, string prop, string child)
        => el.TryGetProperty(prop, out var inner) && inner.TryGetProperty(child, out var v)
            ? v.GetString()
            : null;
}
