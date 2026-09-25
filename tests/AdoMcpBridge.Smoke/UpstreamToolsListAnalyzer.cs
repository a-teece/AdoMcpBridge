using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Smoke;

/// <summary>
/// Diagnostic analyser for a live MCP <c>tools/list</c> response. Turns the raw
/// response body (plain JSON or SSE-framed) into a flat, inspectable report of every
/// advertised tool and the shape of its input schema.
///
/// The point is to confirm — against the CURRENT Microsoft Remote MCP surface, which has
/// since consolidated its tools into <c>action</c>-dispatcher shapes — whether the bridge's
/// upstream-shape assumptions still hold: whether any tool still advertises a bare
/// <c>{"type":"object"}</c> schema (the cause of guessed parameter names), which tools are
/// now action dispatchers, and what array/scalar parameters <c>wit_work_item_write</c> and
/// friends actually expose (the inputs to <c>UpstreamSchemaPatches</c> and
/// <c>WitWorkItemWriteArgumentNormalizer</c>).
/// </summary>
internal static class UpstreamToolsListAnalyzer
{
    /// <summary>
    /// Extracts the JSON-RPC <c>result</c> element from a tools/list response body,
    /// tolerating both a plain-JSON body and an SSE stream whose <c>data:</c> line carries
    /// the JSON-RPC frame. The returned element is cloned and independent of any
    /// <see cref="JsonDocument"/> lifetime.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No parseable JSON-RPC tools/list result was found in the body.
    /// </exception>
    public static JsonElement ExtractResult(string body)
    {
        foreach (var candidate in CandidateJsonPayloads(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("tools", out _))
                {
                    return result.Clone();
                }
            }
            catch (JsonException)
            {
                // Not this line — try the next candidate SSE frame.
            }
        }

        throw new InvalidOperationException(
            "No JSON-RPC tools/list result found in the response body.");
    }

    /// <summary>Analyses a tools/list response body into a per-tool report.</summary>
    public static UpstreamToolsListReport Analyze(string body)
    {
        var result = ExtractResult(body);

        var tools = new List<ToolSummary>();
        if (result.TryGetProperty("tools", out var toolsArray) &&
            toolsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in toolsArray.EnumerateArray())
            {
                tools.Add(Summarize(tool));
            }
        }

        return new UpstreamToolsListReport(tools);
    }

    private static ToolSummary Summarize(JsonElement tool)
    {
        var name = tool.TryGetProperty("name", out var nameEl) &&
                   nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString() ?? string.Empty
            : string.Empty;

        var hasDescription = tool.TryGetProperty("description", out var descEl) &&
                             descEl.ValueKind == JsonValueKind.String &&
                             !string.IsNullOrWhiteSpace(descEl.GetString());

        var hasSchema = tool.TryGetProperty("inputSchema", out var schema) &&
                        schema.ValueKind == JsonValueKind.Object;

        var propertyNames = new List<string>();
        var required = new List<string>();
        var actionValues = new List<string>();

        JsonElement properties = default;
        var hasProperties = hasSchema &&
                            schema.TryGetProperty("properties", out properties) &&
                            properties.ValueKind == JsonValueKind.Object &&
                            properties.EnumerateObject().Any();

        if (hasProperties)
        {
            foreach (var prop in properties.EnumerateObject())
            {
                propertyNames.Add(prop.Name);
            }

            if (properties.TryGetProperty("action", out var action) &&
                action.ValueKind == JsonValueKind.Object &&
                action.TryGetProperty("enum", out var actionEnum) &&
                actionEnum.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in actionEnum.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        actionValues.Add(value.GetString() ?? string.Empty);
                    }
                }
            }
        }

        if (hasSchema &&
            schema.TryGetProperty("required", out var req) &&
            req.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in req.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    required.Add(item.GetString() ?? string.Empty);
                }
            }
        }

        // A "bare object" schema is the symptom behind guessed parameter names: the schema
        // declares an object but advertises no properties for the client to read.
        var isBareObjectSchema = hasSchema && !hasProperties;

        return new ToolSummary(
            name, hasDescription, hasProperties, isBareObjectSchema,
            actionValues.Count > 0, actionValues, propertyNames, required);
    }

    private static IEnumerable<string> CandidateJsonPayloads(string body)
    {
        var trimmed = body.TrimStart();
        var isSse = trimmed.StartsWith("data:", StringComparison.Ordinal) ||
                    trimmed.StartsWith("event:", StringComparison.Ordinal);

        if (!isSse)
        {
            yield return body;
            yield break;
        }

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var json = line["data:".Length..].Trim();
                if (json.Length > 0 && json != "[DONE]")
                {
                    yield return json;
                }
            }
        }
    }
}

/// <summary>The analysed tools/list surface: one <see cref="ToolSummary"/> per advertised tool.</summary>
internal sealed record UpstreamToolsListReport(IReadOnlyList<ToolSummary> Tools)
{
    /// <summary>Number of tools advertised.</summary>
    public int ToolCount => Tools.Count;

    /// <summary>How many tools advertise an object schema with no properties.</summary>
    public int BareObjectSchemaCount => Tools.Count(t => t.IsBareObjectSchema);

    /// <summary>Finds a tool by exact name, or <see langword="null"/> if absent.</summary>
    public ToolSummary? Find(string name) =>
        Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    /// <summary>A compact, human-readable table suitable for test output.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"tools/list surface: {ToolCount} tool(s)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"bare-object schemas (no properties): {BareObjectSchemaCount}");
        sb.AppendLine();
        sb.AppendLine("name | props | bare | action-values | required");
        sb.AppendLine("-----|-------|------|---------------|---------");
        foreach (var t in Tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var actions = t.ActionValues.Count > 0 ? string.Join("/", t.ActionValues) : "-";
            var required = t.Required.Count > 0 ? string.Join(",", t.Required) : "-";
            var bare = t.IsBareObjectSchema ? "YES" : "-";
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"{t.Name} | {t.PropertyNames.Count} | {bare} | {actions} | {required}");
        }

        return sb.ToString();
    }
}

/// <summary>The shape of a single advertised tool, reduced to the facts we care about.</summary>
internal sealed record ToolSummary(
    string Name,
    bool HasDescription,
    bool HasProperties,
    bool IsBareObjectSchema,
    bool HasActionParam,
    IReadOnlyList<string> ActionValues,
    IReadOnlyList<string> PropertyNames,
    IReadOnlyList<string> Required);
