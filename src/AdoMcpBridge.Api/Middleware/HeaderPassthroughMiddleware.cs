namespace AdoMcpBridge.Api.Middleware;

internal sealed class HeaderPassthroughMiddleware
{
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-MCP-Toolsets",
        "X-MCP-Readonly",
        "X-MCP-Tools",
        "X-MCP-Insiders",
    };

    private readonly RequestDelegate _next;

    public HeaderPassthroughMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var toStrip = context.Request.Headers.Keys
            .Where(h => h.StartsWith("X-", StringComparison.OrdinalIgnoreCase) && !Allowlist.Contains(h))
            .ToArray();
        foreach (var h in toStrip)
        {
            context.Request.Headers.Remove(h);
        }
        await _next(context);
    }
}
