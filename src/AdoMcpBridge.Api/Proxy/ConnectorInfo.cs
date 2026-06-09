namespace AdoMcpBridge.Api.Proxy;

internal static class ConnectorInfoEndpoint
{
    public static IEndpointRouteBuilder MapConnectorInfo(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/connector-info.json", (HttpContext ctx, IConfiguration config) =>
        {
            var issuer = config["AdoMcp:Issuer"] ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var info = new
            {
                name = "Azure DevOps (via Bridge)",
                description = "Use Azure DevOps tools (repos, work items, pipelines) through the ADO MCP Bridge.",
                auth_metadata_url = $"{issuer.TrimEnd('/')}/.well-known/oauth-authorization-server",
            };
            return Results.Json(info);
        });
        return endpoints;
    }
}
