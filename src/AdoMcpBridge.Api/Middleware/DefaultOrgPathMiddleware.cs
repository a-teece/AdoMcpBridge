using AdoMcpBridge.Api.Options;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Api.Middleware;

/// <summary>
/// Injects the configured default organization into the upstream URL path for the
/// standard (upstream-proxied) tools, which take their organization from the path
/// (<c>mcp.dev.azure.com/{org}</c>) rather than from a tool argument. When the client
/// connects to a bare <c>/mcp</c> (no org segment) and a default is configured, the
/// request path is rewritten to <c>/mcp/{DefaultOrganization}</c> so YARP forwards it
/// to the default org. A request that already carries an org segment (e.g.
/// <c>/mcp/Contoso</c>) is left untouched, and with no default configured a bare
/// <c>/mcp</c> behaves exactly as before — this is purely additive.
/// Runs after <c>HeaderPassthroughMiddleware</c> and before routing so the rewritten
/// path is what YARP routes on.
/// </summary>
internal sealed class DefaultOrgPathMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _defaultOrganization;

    public DefaultOrgPathMiddleware(RequestDelegate next, IOptions<AdoMcpOptions> options)
    {
        _next = next;
        _defaultOrganization = options.Value.DefaultOrganization;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(_defaultOrganization))
        {
            var path = context.Request.Path;

            // Only a BARE /mcp (no org segment) gets the default injected. StartsWithSegments
            // treats "/mcp" and "/mcp/" as the branch root; any deeper path already names an org.
            if (path.Equals("/mcp", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/mcp/", StringComparison.OrdinalIgnoreCase))
            {
                context.Request.Path = "/mcp/" + _defaultOrganization;
            }
        }

        await _next(context);
    }
}
