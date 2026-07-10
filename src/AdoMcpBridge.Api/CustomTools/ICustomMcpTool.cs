using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools;

internal interface ICustomMcpTool
{
    string Name { get; }
    string Description { get; }

    /// <summary>JSON Schema object describing the tool's input arguments.</summary>
    object InputSchema { get; }

    Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct);
}

internal sealed record McpToolResult(string Text, bool IsError = false);
