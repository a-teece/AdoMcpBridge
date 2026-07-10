using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Intercepts MCP JSON-RPC requests on the /mcp branch and handles custom
/// tool calls locally, short-circuiting the YARP reverse proxy.
/// Also injects custom tool definitions into tools/list responses from upstream.
/// Must be placed after <c>EntraTokenSwapMiddleware</c> (auth is required) and
/// before the YARP endpoint.
/// </summary>
internal sealed class CustomToolMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<ICustomMcpTool> _tools;
    private readonly ILogger<CustomToolMiddleware> _logger;

    public CustomToolMiddleware(
        RequestDelegate next,
        IEnumerable<ICustomMcpTool> tools,
        ILogger<CustomToolMiddleware> logger)
    {
        _next = next;
        _tools = tools.ToList();
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Enable buffering so the body can be read and then rewound for YARP.
        context.Request.EnableBuffering();

        JsonDocument? doc = null;
        try
        {
            doc = await TryParseBodyAsync(context.Request, context.RequestAborted);
        }
        catch
        {
            // Ignore parse failures — not a JSON-RPC request, pass through.
        }

        if (doc is null)
        {
            context.Request.Body.Seek(0, SeekOrigin.Begin);
            await _next(context);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            var id = root.TryGetProperty("id", out var idEl) ? idEl : (JsonElement?)null;

            if (method == "tools/call" &&
                root.TryGetProperty("params", out var p) &&
                p.TryGetProperty("name", out var nameEl))
            {
                var toolName = nameEl.GetString();
                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool is not null)
                {
                    var args = p.TryGetProperty("arguments", out var a) ? a : default;
                    await HandleToolCallAsync(context, tool, args, id);
                    return;
                }
            }

            if (method == "tools/list")
            {
                await HandleToolsListAsync(context);
                return;
            }
        }

        context.Request.Body.Seek(0, SeekOrigin.Begin);
        await _next(context);
    }

    private async Task HandleToolCallAsync(
        HttpContext context, ICustomMcpTool tool, JsonElement arguments, JsonElement? id)
    {
        _logger.LogInformation("Handling custom tool call: {Tool}", tool.Name);

        McpToolResult result;
        try
        {
            result = await tool.InvokeAsync(arguments, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in custom tool {Tool}", tool.Name);
            await JsonRpcHelpers.WriteErrorAsync(
                context.Response, id, -32603, "Internal error", context.RequestAborted);
            return;
        }

        await JsonRpcHelpers.WriteResultAsync(context.Response, id, result, context.RequestAborted);
    }

    private async Task HandleToolsListAsync(HttpContext context)
    {
        // Buffer the upstream response so we can inject our tool definitions.
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            context.Request.Body.Seek(0, SeekOrigin.Begin);
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        var responseBytes = buffer.ToArray();

        var modified = JsonRpcHelpers.InjectToolsIntoListResponse(responseBytes, _tools);

        context.Response.ContentLength = modified.Length;
        await originalBody.WriteAsync(modified, context.RequestAborted);
    }

    private static async Task<JsonDocument?> TryParseBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength == 0) return null;
        var contentType = request.ContentType ?? string.Empty;
        if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            return await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
