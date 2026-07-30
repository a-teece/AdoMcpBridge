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

    // Azure DevOps silently ignores a /fields/System.Parent patch — the parent/child
    // link lives in the relations API, not the fields collection. Left alone the write
    // looks like it succeeded but the parent never changed, so we reject it loudly and
    // name the correct primitive rather than let a caller ship a wrong (or missing) parent.
    private const string ParentFieldRefName = "System.Parent";
    private const string ParentFieldPatchPath = "/fields/System.Parent";

    private const string ParentWriteGuidance =
        "'System.Parent' cannot be set through a work-item field write — Azure DevOps silently " +
        "ignores it and the parent never changes. Set the parent by adding a hierarchy relation " +
        "instead: PATCH the work item with op 'add', path '/relations/-', value " +
        "{ \"rel\": \"System.LinkTypes.Hierarchy-Reverse\", \"url\": \"<parent work item REST URL>\" } " +
        "(or use your MCP client's work-item link tool). Remove 'System.Parent' from this write.";

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
            RejectParentFieldWrite(normalized);
            if (!ReferenceEquals(normalized, value))
            {
                arguments[name] = normalized;
                changed = true;
            }
        }

        return changed ? Encoding.UTF8.GetBytes(node.ToJsonString()) : null;
    }

    /// <summary>
    /// Fails the write if any entry targets <c>System.Parent</c> — either the
    /// name/value shape (<c>{ "name": "System.Parent" }</c>) or the JSON-Patch shape
    /// (<c>{ "path": "/fields/System.Parent" }</c>) — which ADO would silently drop.
    /// Runs against the already-normalised array so a parent hidden inside a
    /// JSON-encoded string argument is still caught.
    /// </summary>
    /// <exception cref="WitWorkItemWriteArgumentException">A parent field write was found.</exception>
    private static void RejectParentFieldWrite(JsonArray array)
    {
        foreach (var item in array)
        {
            if (item is not JsonObject obj) continue;

            if (StringPropertyEquals(obj, "name", ParentFieldRefName) ||
                StringPropertyEquals(obj, "path", ParentFieldPatchPath))
            {
                throw new WitWorkItemWriteArgumentException(ParentWriteGuidance);
            }
        }
    }

    private static bool StringPropertyEquals(JsonObject obj, string property, string expected)
        => obj.TryGetPropertyValue(property, out var node) &&
           node is JsonValue value &&
           value.TryGetValue<string>(out var actual) &&
           string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

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
