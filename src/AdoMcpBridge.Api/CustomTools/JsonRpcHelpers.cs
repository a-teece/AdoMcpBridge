using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AdoMcpBridge.Api.CustomTools;

internal static class JsonRpcHelpers
{
    private static readonly JsonSerializerOptions _pretty = new() { WriteIndented = false };

    /// <summary>
    /// The MCP <c>notifications/tools/list_changed</c> message, serialised as a single
    /// JSON-RPC notification (no <c>id</c>). Injected into stale sessions after a redeploy
    /// so clients re-fetch the current tool list.
    /// </summary>
    public const string ToolsListChangedNotification =
        "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}";

    /// <summary>
    /// Patches an <c>initialize</c> response so it advertises
    /// <c>result.capabilities.tools.listChanged = true</c>, creating the
    /// <c>capabilities</c> and/or <c>tools</c> objects if upstream omitted them. Handles
    /// both plain-JSON and SSE bodies. Returns the original bytes unchanged if the body
    /// cannot be parsed or carries no <c>result</c>.
    /// </summary>
    public static byte[] PatchInitializeListChanged(byte[] responseBytes)
    {
        try
        {
            var text = Encoding.UTF8.GetString(responseBytes);
            var trimmed = text.TrimStart();
            var isSse = trimmed.StartsWith("data:", StringComparison.Ordinal) ||
                        trimmed.StartsWith("event:", StringComparison.Ordinal);

            return isSse
                ? PatchInitializeSse(text)
                : PatchInitializeJson(responseBytes);
        }
        catch
        {
            // Never break the proxy — return the original bytes unchanged.
            return responseBytes;
        }
    }

    /// <summary>
    /// If <paramref name="responseBytes"/> is an SSE body, prepends a
    /// <c>notifications/tools/list_changed</c> frame BEFORE the existing frame(s) and
    /// returns <see langword="true"/>. If the body is plain JSON (not SSE) the bytes are
    /// returned unchanged and the result is <see langword="false"/> — a second message
    /// cannot be safely appended to a bare JSON object now that MCP has removed batching.
    /// </summary>
    public static bool TryPrependToolsListChangedFrame(byte[] responseBytes, out byte[] result)
    {
        var text = Encoding.UTF8.GetString(responseBytes);
        var trimmed = text.TrimStart();
        var isSse = trimmed.StartsWith("data:", StringComparison.Ordinal) ||
                    trimmed.StartsWith("event:", StringComparison.Ordinal);

        if (!isSse)
        {
            result = responseBytes;
            return false;
        }

        var frame = "event: message\ndata: " + ToolsListChangedNotification + "\n\n";
        result = Encoding.UTF8.GetBytes(frame + text);
        return true;
    }

    private static byte[] PatchInitializeJson(byte[] responseBytes)
    {
        var node = JsonNode.Parse(responseBytes);
        if (node?["result"] is not JsonObject result)
        {
            return responseBytes;
        }

        PatchCapabilitiesListChanged(result);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    private static byte[] PatchInitializeSse(string text)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var json = line["data:".Length..].Trim();
                if (json is not ("[DONE]" or ""))
                {
                    var node = JsonNode.Parse(json);
                    if (node?["result"] is JsonObject result)
                    {
                        PatchCapabilitiesListChanged(result);
                        sb.Append("data: ");
                        sb.AppendLine(node.ToJsonString());
                        continue;
                    }
                }
            }

            sb.AppendLine(line);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void PatchCapabilitiesListChanged(JsonObject result)
    {
        if (result["capabilities"] is not JsonObject capabilities)
        {
            capabilities = new JsonObject();
            result["capabilities"] = capabilities;
        }

        if (capabilities["tools"] is not JsonObject tools)
        {
            tools = new JsonObject();
            capabilities["tools"] = tools;
        }

        tools["listChanged"] = true;
    }

    public static async Task WriteResultAsync(
        HttpResponse response, JsonElement? requestId, McpToolResult result, CancellationToken ct)
    {
        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = result.Text,
            }
        };

        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId.HasValue ? JsonNode.Parse(requestId.Value.GetRawText()) : null,
            ["result"] = new JsonObject
            {
                ["content"] = content,
                ["isError"] = result.IsError,
            },
        };

        response.ContentType = "application/json";
        response.StatusCode = 200;
        await response.WriteAsync(envelope.ToJsonString(_pretty), Encoding.UTF8, ct);
    }

    public static async Task WriteErrorAsync(
        HttpResponse response, JsonElement? requestId, int code, string message, CancellationToken ct)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId.HasValue ? JsonNode.Parse(requestId.Value.GetRawText()) : null,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };

        response.ContentType = "application/json";
        response.StatusCode = 200; // JSON-RPC errors are HTTP 200
        await response.WriteAsync(envelope.ToJsonString(_pretty), Encoding.UTF8, ct);
    }

    /// <summary>
    /// Injects <paramref name="extraTools"/> into a JSON-RPC tools/list response body.
    /// Returns the original bytes unmodified if the response cannot be parsed or is not
    /// a success result (e.g. it is an error response, or it is an SSE stream that
    /// happens to start with an event prefix rather than a JSON object).
    /// </summary>
    public static byte[] InjectToolsIntoListResponse(byte[] responseBytes, IEnumerable<ICustomMcpTool> extraTools)
    {
        try
        {
            // Detect SSE: lines starting with "data:" — parse the first data event.
            var text = Encoding.UTF8.GetString(responseBytes);
            bool isSse = text.TrimStart().StartsWith("data:", StringComparison.Ordinal) ||
                         text.TrimStart().StartsWith("event:", StringComparison.Ordinal);

            if (isSse)
            {
                return InjectIntoSseResponse(text, extraTools);
            }

            return InjectIntoJsonResponse(responseBytes, extraTools);
        }
        catch
        {
            // Never break the proxy — return the original bytes unchanged.
            return responseBytes;
        }
    }

    private static byte[] InjectIntoJsonResponse(byte[] responseBytes, IEnumerable<ICustomMcpTool> extraTools)
    {
        using var doc = JsonDocument.Parse(responseBytes);
        var root = doc.RootElement;

        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("tools", out _))
        {
            return responseBytes;
        }

        var node = JsonNode.Parse(responseBytes)!;
        var toolsArray = node["result"]!["tools"]!.AsArray();
        UpstreamSchemaPatches.PatchToolsArray(toolsArray);
        foreach (var tool in extraTools)
        {
            toolsArray.Add(BuildToolDefinition(tool));
        }

        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    private static byte[] InjectIntoSseResponse(string text, IEnumerable<ICustomMcpTool> extraTools)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var json = line["data:".Length..].Trim();
                if (json is "[DONE]" or "")
                {
                    sb.AppendLine(line);
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("tools", out _))
                {
                    var node = JsonNode.Parse(json)!;
                    var toolsArray = node["result"]!["tools"]!.AsArray();
                    UpstreamSchemaPatches.PatchToolsArray(toolsArray);
                    foreach (var tool in extraTools)
                    {
                        toolsArray.Add(BuildToolDefinition(tool));
                    }

                    sb.Append("data: ");
                    sb.AppendLine(node.ToJsonString());
                    continue;
                }
            }

            sb.AppendLine(line);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static JsonObject BuildToolDefinition(ICustomMcpTool tool)
    {
        var obj = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["inputSchema"] = JsonNode.Parse(JsonSerializer.Serialize(tool.InputSchema)),
        };
        if (tool.Annotations is not null)
            obj["annotations"] = JsonNode.Parse(JsonSerializer.Serialize(tool.Annotations));
        return obj;
    }
}
