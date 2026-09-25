using System.Text.Json;

namespace AdoMcpBridge.Smoke;

/// <summary>
/// Writes the raw tools/list surface to disk so the current upstream shape can be inspected
/// offline (diffed against the bridge's assumptions, grepped for a specific tool's schema).
/// Destination directory is <c>ADOMCP_SMOKE_DUMP_DIR</c> when set, otherwise the test
/// binary's base directory. Writing never throws — a diagnostic must not fail on an IO issue.
/// </summary>
internal static class UpstreamSurfaceDump
{
    public const string DumpDirVar = "ADOMCP_SMOKE_DUMP_DIR";

    private const string RawFileName = "upstream-toolslist.json";
    private const string SummaryFileName = "upstream-toolslist-summary.txt";

    /// <summary>
    /// Writes the raw body (and, when it parses, a pretty-printed copy) plus the analysed
    /// summary. Returns a status line naming the directory written to, or the failure reason.
    /// </summary>
    public static string Write(string rawBody, UpstreamToolsListReport report)
    {
        var dir = Environment.GetEnvironmentVariable(DumpDirVar);
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = AppContext.BaseDirectory;
        }

        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, RawFileName), Prettify(rawBody));
            File.WriteAllText(Path.Combine(dir, SummaryFileName), report.ToText());
            return $"wrote {RawFileName} + {SummaryFileName} to {dir}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"could not write dump to '{dir}': {ex.Message}";
        }
    }

    // Pretty-print when the body is a single JSON document; otherwise (e.g. an SSE stream)
    // keep the bytes verbatim so the on-the-wire framing is preserved for inspection.
    private static string Prettify(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };
}
