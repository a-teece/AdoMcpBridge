using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AdoMcpBridge.Api.CustomTools;

internal static class JsonRpcHelpers
{
    private static readonly JsonSerializerOptions _pretty = new() { WriteIndented = false };

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

    private static JsonObject BuildToolDefinition(ICustomMcpTool tool) =>
        new()
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["inputSchema"] = JsonNode.Parse(JsonSerializer.Serialize(tool.InputSchema)),
        };
}
