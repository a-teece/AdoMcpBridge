namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Field-level guard for the native scalar write tools (<c>ado_bridge_wit_update</c>,
/// <c>ado_bridge_wit_update_batch</c>). Long-text fields (the shared
/// <see cref="BasicToolGuardrails.LongTextFieldRefNames"/> list) are never written inline —
/// they need the slot path, which applies format handling and round-trip verification — and
/// <c>System.Parent</c> is never written as a field because Azure DevOps silently ignores it
/// (the parent lives in the relations collection), and <c>System.History</c> (the discussion
/// field) is never written inline because comments go through <c>ado_bridge_add_comment</c>.
/// All are rejected whatever the op, with
/// text naming the tool to use instead. Matching is case-insensitive and ignores surrounding
/// whitespace, since ADO resolves field names that way.
/// </summary>
internal static class WorkItemFieldGuard
{
    private const string ParentFieldRefName = "System.Parent";
    private const string HistoryFieldRefName = "System.History";

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

        // System.History is the discussion field: an inline write posts a comment with no format
        // handling (the same corruption ado_bridge_add_comment exists to avoid). Guard-local so
        // the shared long-text list — and upstream's wit_work_item_write guard — are unchanged.
        if (string.Equals(name, HistoryFieldRefName, StringComparison.OrdinalIgnoreCase))
        {
            throw new CallerArgumentException(
                "'System.History' is the work item's discussion (comment) field and cannot be " +
                "written inline — the comment's formatting would be corrupted. Use " +
                "'ado_bridge_add_comment' to post a comment instead, and remove 'System.History' " +
                "from this update.");
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
