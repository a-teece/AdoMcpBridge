using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Belt-and-braces compatibility shim for the same upstream schema defect that
/// <see cref="UpstreamSchemaPatches"/> patches at tools/list: coerces
/// <c>wit_work_item_write</c>'s array-valued parameters (<c>fields</c>, <c>updates</c>,
/// <c>items</c>, <c>batchUpdates</c>) into real JSON arrays before the request is
/// forwarded upstream. This covers clients that ignore the patched schema, or that
/// cached the unpatched one from an earlier session.
///
/// Never guesses past ambiguity — a string that isn't valid JSON, or a value that can't
/// be normalised to an array, fails loudly naming the parameter rather than silently
/// dropping or reshaping data that would corrupt a work item write.
///
/// Delete alongside <see cref="UpstreamSchemaPatches"/> once upstream fixes its schema.
/// </summary>
internal static class WitWorkItemWriteArgumentNormalizer
{
    public const string ToolName = "wit_work_item_write";

    private static readonly string[] ArrayParameterNames = ["fields", "updates", "items", "batchUpdates"];

    /// <summary>
    /// Returns the rewritten JSON-RPC request body if any array parameter needed
    /// normalisation, or <see langword="null"/> if the request can be forwarded
    /// unmodified.
    /// </summary>
    /// <exception cref="WitWorkItemWriteArgumentException">
    /// A parameter could not be normalised to an array of objects.
    /// </exception>
    public static byte[]? NormalizeRequestBody(JsonElement root)
    {
        var node = JsonNode.Parse(root.GetRawText())!;
        if (node["params"]?["arguments"] is not JsonObject arguments) return null;

        var changed = false;
        foreach (var name in ArrayParameterNames)
        {
            if (!arguments.TryGetPropertyValue(name, out var value) || value is null) continue;

            JsonArray normalized = NormalizeArrayParameter(name, value);
            if (!ReferenceEquals(normalized, value))
            {
                arguments[name] = normalized;
                changed = true;
            }
        }

        return changed ? Encoding.UTF8.GetBytes(node.ToJsonString()) : null;
    }

    private static JsonArray NormalizeArrayParameter(string paramName, JsonNode value)
    {
        if (value is JsonArray existingArray) return StringifyNonStringValues(existingArray);

        // Unwrap a JSON-encoded string, tolerating a client that double-encoded it.
        JsonNode? current = value;
        for (var i = 0; i < 3 && current is JsonValue stringCandidate && stringCandidate.TryGetValue<string>(out var s); i++)
        {
            try
            {
                current = JsonNode.Parse(s);
            }
            catch (JsonException ex)
            {
                throw new WitWorkItemWriteArgumentException(
                    $"'{paramName}' was a string but not valid JSON: {Truncate(s)}", ex);
            }
        }

        if (current is JsonObject singleOrMap)
        {
            if (singleOrMap.ContainsKey("name") && singleOrMap.ContainsKey("value"))
            {
                return StringifyNonStringValues(new JsonArray { singleOrMap.DeepClone() });
            }

            var mapped = new JsonArray();
            foreach (var pair in singleOrMap)
            {
                mapped.Add(new JsonObject { ["name"] = pair.Key, ["value"] = pair.Value?.DeepClone() });
            }
            return StringifyNonStringValues(mapped);
        }

        if (current is JsonArray unwrappedArray) return StringifyNonStringValues(unwrappedArray);

        throw new WitWorkItemWriteArgumentException($"'{paramName}' could not be normalised to an array");
    }

    /// <summary>
    /// ADO's field-value validator rejects bare JSON numbers/booleans for some fields
    /// (e.g. Microsoft.VSTS.Common.StackRank must be <c>"100"</c>, not <c>100</c>).
    /// Returns the same array instance, unmodified, when no entry needs stringifying —
    /// callers rely on that reference equality to detect a no-op.
    /// </summary>
    private static JsonArray StringifyNonStringValues(JsonArray array)
    {
        var needsChange = array.Any(item =>
            item is JsonObject obj &&
            obj.TryGetPropertyValue("value", out var v) &&
            v is not null &&
            v.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False);

        if (!needsChange) return array;

        var result = new JsonArray();
        foreach (var item in array)
        {
            if (item is JsonObject obj &&
                obj.TryGetPropertyValue("value", out var v) &&
                v is not null &&
                v.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                var clone = (JsonObject)obj.DeepClone()!;
                clone["value"] = JsonValue.Create(v.ToJsonString());
                result.Add(clone);
            }
            else
            {
                result.Add(item?.DeepClone());
            }
        }

        return result;
    }

    private static string Truncate(string s) => s.Length <= 120 ? s : s[..120];
}

internal sealed class WitWorkItemWriteArgumentException(string message, Exception? inner = null)
    : Exception(message, inner);
