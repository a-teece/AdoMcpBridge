using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools;

internal interface ICustomMcpTool
{
    string Name { get; }
    string Description { get; }

    /// <summary>JSON Schema object describing the tool's input arguments.</summary>
    object InputSchema { get; }

    /// <summary>
    /// MCP tool annotations (e.g. <c>readOnlyHint</c>).  Returns <see langword="null"/>
    /// by default; override to emit an <c>annotations</c> object in tools/list.
    /// </summary>
    object? Annotations => null;

    Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct);
}

internal sealed record McpToolResult(string Text, bool IsError = false);
