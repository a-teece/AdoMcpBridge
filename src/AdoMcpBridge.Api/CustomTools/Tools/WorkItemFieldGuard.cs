namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Field-level guard for the native scalar write tools (<c>ado_bridge_wit_update</c>,
/// <c>ado_bridge_wit_update_batch</c>). Long-text fields (the shared
/// <see cref="BasicToolGuardrails.LongTextFieldRefNames"/> list) are never written inline —
/// they need the slot path, which applies format handling and round-trip verification — and
/// <c>System.Parent</c> is never written as a field because Azure DevOps silently ignores it
/// (the parent lives in the relations collection). Both are rejected whatever the op, with
/// text naming the tool to use instead. Matching is case-insensitive and ignores surrounding
/// whitespace, since ADO resolves field names that way.
/// </summary>
internal static class WorkItemFieldGuard
{
    private const string ParentFieldRefName = "System.Parent";

    /// <exception cref="CallerArgumentException">The field may not be written inline.</exception>
    public static void ThrowIfForbidden(string fieldRefName)
    {
        var name = fieldRefName.Trim();

        if (string.Equals(name, ParentFieldRefName, StringComparison.OrdinalIgnoreCase))
        {
            throw new CallerArgumentException(
                "'System.Parent' cannot be written as a work-item field — Azure DevOps silently " +
                "ignores it and the parent never changes. Use 'ado_bridge_wit_link' to add or " +
                "remove the parent link instead, and remove 'System.Parent' from this update.");
        }

        foreach (var longText in BasicToolGuardrails.LongTextFieldRefNames)
        {
            if (string.Equals(name, longText, StringComparison.OrdinalIgnoreCase))
            {
                throw new CallerArgumentException(
                    $"'{longText}' is a long-text (HTML) work-item field and cannot be written " +
                    "inline. Call 'ado_bridge_create_upload_slot', upload the body, then " +
                    $"'ado_bridge_write_field_from_slot' to write it into {longText}. Remove " +
                    $"'{longText}' from this update (other fields are fine).");
            }
        }
    }
}
