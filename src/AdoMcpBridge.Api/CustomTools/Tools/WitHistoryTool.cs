using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Returns a work item's change history (its update records), newest first, as a slim list of
/// field changes per update. Long-text old/new values are stubbed, never inlined.
/// </summary>
internal sealed class WitHistoryTool : ICustomMcpTool
{
    internal const int DefaultTop = 20;
    internal const int MaxTop = 100;

    // ADO rejects $top above 200 on the updates endpoint with HTTP 400 (verified live 2026-10-06).
    internal const int AdoPageSize = 200;

    // Hard ceiling on the history read: 50 pages of 200 = 10,000 updates (link-only updates
    // count). Longer histories are refused rather than truncated.
    internal const int MaxAdoPages = 50;
    internal const int MaxUpdates = MaxAdoPages * AdoPageSize;

    private readonly IAdoRestClient _ado;
    private readonly ILogger<WitHistoryTool> _logger;

    public WitHistoryTool(IAdoRestClient ado, ILogger<WitHistoryTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_history";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Returns a work item's change history, newest first: one entry per update " +
        "with rev, revisedBy, revisedDate, changedDate and the field changes (field, old, new), plus ADO's " +
        "'relations' (added/removed links) when the update changed links. changedDate is when the change " +
        "was made (absent on link-only updates); revisedDate is when that revision was superseded, so the " +
        "newest entry may carry a far-future sentinel date. Any old/new text over " +
        $"{WorkItemSlimProjector.OversizeFieldCharCeiling} characters is replaced by " +
        "{\"stubbed\":true,\"length\":N} — read the current value with ado_bridge_download_field. " +
        $"Page with top/skip (skip counts from the newest update). Items with more than {MaxUpdates} " +
        "updates are refused with an error rather than returned partially.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            id = new { type = "integer", description = "Work item id." },
            top = new
            {
                type = "integer",
                description = $"Maximum number of updates to return (default {DefaultTop}; values above {MaxTop} are capped at {MaxTop}).",
            },
            skip = new
            {
                type = "integer",
                description = "Number of most recent updates to skip for paging (default 0).",
            },
        },
        required = new[] { "organization", "project", "id" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var id = ToolArgs.RequireInt(arguments, "id");
        if (id < 1)
            throw new CallerArgumentException("'id' must be a positive work item id.");
        var top = ToolArgs.GetInt(arguments, "top") ?? DefaultTop;
        var skip = ToolArgs.GetInt(arguments, "skip") ?? 0;
        if (top < 1)
            throw new CallerArgumentException("'top' must be at least 1.");
        if (skip < 0)
            throw new CallerArgumentException("'skip' must be 0 or greater.");

        top = Math.Min(top, MaxTop);

        _logger.LogInformation(
            "ado_bridge_wit_history: {Org}/{Project} id={Id} top={Top} skip={Skip}",
            org, project, id, top, skip);

        // ADO returns updates oldest first with no way to reverse the order, so read every
        // page (at most MaxAdoPages); newest-first paging is then applied here.
        var updates = new List<JsonElement>();
        var complete = false;
        try
        {
            // A short (or empty) page ends the history. Pages are addressed by index, not by
            // rows received, and the page count is capped, so a misbehaving response (e.g. ADO
            // ignoring $skip and repeating a full page) cannot loop forever.
            for (var pageIndex = 0; pageIndex < MaxAdoPages && !complete; pageIndex++)
            {
                var rows = await ReadPageAsync(org, project, id, AdoPageSize, pageIndex * AdoPageSize, ct)
                    .ConfigureAwait(false);
                updates.AddRange(rows);
                complete = rows.Count < AdoPageSize;
            }

            // Every page was full: a one-row probe past the ceiling distinguishes a history of
            // exactly MaxUpdates from a longer one.
            if (!complete)
                complete = (await ReadPageAsync(org, project, id, 1, MaxUpdates, ct).ConfigureAwait(false)).Count == 0;
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

        // Never return a partial list: it would hold the OLDEST updates, not the newest.
        if (!complete)
            return new McpToolResult(
                $"Work item {id} has more than {MaxUpdates} updates, the most this tool reads; its history " +
                "cannot be ordered newest-first, so no (partial) history was returned.", IsError: true);

        updates.Reverse();
        return new McpToolResult(BuildSlimJson(updates.Skip(skip).Take(top)));
    }

    private async Task<List<JsonElement>> ReadPageAsync(
        string org, string project, int id, int top, int skip, CancellationToken ct)
    {
        var page = await _ado.GetWorkItemUpdatesAsync(org, project, id, top, skip, ct).ConfigureAwait(false);
        return page.TryGetProperty("value", out var value) ? value.EnumerateArray().ToList() : [];
    }

    internal static string BuildSlimJson(IEnumerable<JsonElement> updates)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartArray();
            foreach (var update in updates)
                WriteUpdate(writer, update);
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteUpdate(Utf8JsonWriter writer, JsonElement update)
    {
        writer.WriteStartObject();
        writer.WriteNumber("rev", update.GetProperty("rev").GetInt32());

        if (update.TryGetProperty("revisedBy", out var by) && by.ValueKind == JsonValueKind.Object &&
            by.TryGetProperty("displayName", out var name))
            writer.WriteString("revisedBy", name.GetString());
        else
            writer.WriteNull("revisedBy");

        if (update.TryGetProperty("revisedDate", out var date))
            writer.WriteString("revisedDate", date.GetString());
        else
            writer.WriteNull("revisedDate");

        // revisedDate is when the revision was superseded; System.ChangedDate's new value is
        // when this change was made. Link-only updates carry no fields, so omit it there.
        var hasFields = update.TryGetProperty("fields", out var fields);
        if (hasFields && fields.TryGetProperty("System.ChangedDate", out var changed) &&
            changed.TryGetProperty("newValue", out var changedDate))
        {
            writer.WritePropertyName("changedDate");
            changedDate.WriteTo(writer);
        }

        writer.WritePropertyName("changes");
        writer.WriteStartArray();
        if (hasFields)
        {
            foreach (var field in fields.EnumerateObject())
            {
                writer.WriteStartObject();
                writer.WriteString("field", field.Name);
                WriteValue(writer, "old", field.Value, "oldValue");
                WriteValue(writer, "new", field.Value, "newValue");
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();

        // Link changes are passed through in ADO's own { added, removed } shape.
        if (update.TryGetProperty("relations", out var relations))
        {
            writer.WritePropertyName("relations");
            relations.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, JsonElement change, string adoName)
    {
        if (!change.TryGetProperty(adoName, out var value))
        {
            writer.WriteNull(name);
            return;
        }

        if (value.ValueKind == JsonValueKind.String &&
            value.GetString() is { Length: > WorkItemSlimProjector.OversizeFieldCharCeiling } text)
        {
            writer.WriteStartObject(name);
            writer.WriteBoolean("stubbed", true);
            writer.WriteNumber("length", text.Length);
            writer.WriteEndObject();
            return;
        }

        writer.WritePropertyName(name);
        value.WriteTo(writer);
    }
}
