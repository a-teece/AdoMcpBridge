namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Shared text and field list for the guardrails that keep long-text work-item content
/// off upstream's basic write tools. Upstream applies no format detection or escaping
/// (contrast <see cref="AdoFieldEscaper"/>, which the native tools run), so even a short
/// Markdown or HTML body written through <c>wit_work_item_write</c> /
/// <c>wit_work_item_comment_write</c> lands mangled in Azure DevOps and cannot be
/// recovered from the stored value.
///
/// The same wording is used in three places so the caller sees one consistent story:
/// steered in the tools/list descriptions (<see cref="UpstreamSchemaPatches"/>), enforced
/// for field writes (<see cref="WitWorkItemWriteArgumentNormalizer"/>) and for comment
/// writes (<c>CustomToolMiddleware</c>).
/// </summary>
internal static class BasicToolGuardrails
{
    public const string CommentWriteToolName = "wit_work_item_comment_write";

    /// <summary>
    /// Long-text (HTML-bodied) work-item fields. Any non-empty write to one of these
    /// through the basic tool is rejected; matching is case-insensitive.
    /// </summary>
    public static readonly string[] LongTextFieldRefNames =
    [
        "System.Description",
        "Microsoft.VSTS.TCM.ReproSteps",
        "Microsoft.VSTS.Common.AcceptanceCriteria",
        "Microsoft.VSTS.TCM.SystemInfo",
        "Microsoft.VSTS.CMMI.Symptom",
        "Microsoft.VSTS.CMMI.RootCause",
        "Microsoft.VSTS.CMMI.HowFound",
        "Microsoft.VSTS.CMMI.Justification",
    ];

    public static string LongTextFieldRejection(string fieldRefName) =>
        $"'{fieldRefName}' is a long-text (HTML) work-item field and cannot be written through " +
        "wit_work_item_write — that tool applies no format detection or escaping, so the content " +
        "arrives in Azure DevOps with its formatting corrupted. Write it with the bridge's native " +
        "tools instead: call 'ado_bridge_create_upload_slot', upload the body, then " +
        $"'ado_bridge_write_field_from_slot' to write it into {fieldRefName}. Remove " +
        $"'{fieldRefName}' from this write (other fields in the same call are fine, and clearing " +
        "this field to an empty value through the basic tool is still allowed).";

    public const string CommentWriteRejection =
        "wit_work_item_comment_write is not available through this bridge — it applies no format " +
        "detection or escaping, so comment formatting arrives corrupted. Use the native tool " +
        "'ado_bridge_add_comment' instead: it takes the same work item and body (inline for small " +
        "bodies, or via 'ado_bridge_create_upload_slot' + slotId/sha256 for large ones) and is a " +
        "complete replacement.";

    /// <summary>
    /// Prefixed onto upstream's own <c>wit_work_item_write</c> description at tools/list so the
    /// steering is read before the tool is chosen, not only after a call is rejected.
    /// </summary>
    public static readonly string WitWorkItemWriteDescriptionPrefix =
        "IMPORTANT (ado-mcp-bridge): do NOT use this tool to write long-text fields (" +
        string.Join(", ", LongTextFieldRefNames) +
        "). It applies no format detection or escaping and corrupts their formatting, even for " +
        "short values; the bridge rejects such calls. Write those fields with " +
        "'ado_bridge_create_upload_slot' + 'ado_bridge_write_field_from_slot' instead. Use this " +
        "tool for short scalar fields only (Title, State, Tags, Area/Iteration Path, assignments). ";

    public static readonly string CommentWriteDescriptionPrefix =
        "IMPORTANT (ado-mcp-bridge): do NOT use this tool — the bridge rejects every call to it " +
        "because it corrupts comment formatting. Use the native tool 'ado_bridge_add_comment' " +
        "instead, for comment bodies of any size. ";
}
