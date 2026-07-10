namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Deterministic HTML entity escaping required by ADO's ingest sanitiser.
/// ADO silently strips bare angle-bracket sequences (even inside code spans)
/// on write to long-text fields such as System.Description — WI #95818.
/// </summary>
internal static class AdoFieldEscaper
{
    /// <summary>
    /// Escape markdown for safe storage in an ADO long-text field.
    /// Replacement order: &amp; first, then &lt;, then &gt; — if &amp; came
    /// later the angle-bracket entities would be double-escaped.
    /// Double quotes are intentionally left alone: ADO's own sanitiser
    /// independently encodes them on ingest, so pre-escaping would cause the
    /// &amp; in &amp;quot; to be double-escaped on a subsequent call.
    /// </summary>
    public static string Escape(string markdown) =>
        markdown
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

    /// <summary>
    /// Unescape a value read back from an ADO long-text field, reversing both
    /// <see cref="Escape"/> and ADO's own independent quote encoding.
    /// Replacement order: &amp;lt;/&amp;gt; first, then &amp;quot;, then
    /// &amp;amp; last — inverting &amp;amp; before the others would corrupt
    /// a literal &amp;lt; substring in the original text.
    /// </summary>
    public static string Unescape(string stored) =>
        stored
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&amp;", "&");

    /// <summary>
    /// Strip trailing CR/LF characters so a benign editor-added EOF newline
    /// does not register as a content mismatch.
    /// </summary>
    public static string NormalizeTrailingNewlines(string text) =>
        text.TrimEnd('\r', '\n');

    /// <summary>
    /// Verify that <paramref name="storedValue"/> (read back from ADO after a
    /// write) round-trips correctly to <paramref name="originalMarkdown"/>.
    /// Trailing newlines and ADO's own entity encoding are both normalised
    /// before the comparison.
    /// </summary>
    /// <returns>
    /// <c>isMatch</c> — whether the content matches; <c>charCount</c> — the
    /// normalised length of the original markdown (for diagnostics).
    /// </returns>
    public static (bool isMatch, int charCount) Verify(string originalMarkdown, string storedValue)
    {
        var normalizedOriginal = NormalizeTrailingNewlines(originalMarkdown);
        var normalizedStored   = NormalizeTrailingNewlines(Unescape(storedValue));
        return (normalizedOriginal == normalizedStored, normalizedOriginal.Length);
    }
}
