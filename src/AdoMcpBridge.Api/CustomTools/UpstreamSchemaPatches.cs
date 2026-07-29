using System.Text.Json.Nodes;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Compatibility shim for an upstream schema defect: the Microsoft ADO MCP server's
/// tools/list descriptor for <c>wit_work_item_write</c> omits a <c>type</c>/<c>items</c>
/// declaration on the <c>fields</c> parameter (used by action=create), unlike its sibling
/// array parameters (<c>updates</c>, <c>items</c>, <c>batchUpdates</c>). With no declared
/// type, MCP clients guided only by that schema serialise <c>fields</c> as a JSON-encoded
/// string, which upstream's own validator then rejects with "must be a JSON array of
/// objects ... but received a JSON-encoded string".
///
/// This merges the missing type into the descriptor as it passes through tools/list, so
/// well-behaved clients emit a real array and never hit the bug. See
/// <see cref="WitWorkItemWriteArgumentNormalizer"/> for the belt-and-braces argument
/// coercion applied to clients that ignore the patched schema anyway.
///
/// The patch only fires when <c>fields</c> has no <c>type</c> key, so it becomes an inert
/// no-op the moment upstream fixes its own schema — delete this file at that point.
/// </summary>
internal static class UpstreamSchemaPatches
{
    public static void PatchToolsArray(JsonArray toolsArray)
    {
        foreach (var toolNode in toolsArray)
        {
            if (toolNode is not JsonObject tool) continue;
            if (tool["name"]?.GetValue<string>() != WitWorkItemWriteArgumentNormalizer.ToolName) continue;

            if (tool["inputSchema"]?["properties"]?["fields"] is not JsonObject fields) continue;
            if (fields.ContainsKey("type")) continue; // already typed — upstream fixed it, or shape changed; leave alone

            fields["type"] = new JsonArray { "array", "null" };
            fields["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["name"] = new JsonObject { ["type"] = "string" },
                    ["value"] = new JsonObject(),
                },
                ["required"] = new JsonArray { "name", "value" },
                ["additionalProperties"] = true,
            };
        }
    }
}
