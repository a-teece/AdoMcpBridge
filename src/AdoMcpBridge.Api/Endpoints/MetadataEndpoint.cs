using AdoMcpBridge.Api.Options;
using AdoMcpBridge.Core.OAuth;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Api.Endpoints;

public static class MetadataEndpoint
{
    private static readonly string[] BearerMethods = { "header" };

    public static IEndpointRouteBuilder MapMetadata(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-authorization-server",
            (IOptions<AdoMcpOptions> opts) =>
                Results.Json(AuthorizationServerMetadata.ForIssuer(opts.Value.Issuer)));

        // RFC 9728 protected-resource metadata for the proxied MCP
        // surface. Clients validate `resource` against the URL they
        // connect to, so it must mirror the request path exactly.
        app.MapGet("/.well-known/oauth-protected-resource/mcp/{**path}",
            (string? path, IOptions<AdoMcpOptions> opts) =>
            {
                var issuer = opts.Value.Issuer.TrimEnd('/');
                var resource = string.IsNullOrEmpty(path) ? $"{issuer}/mcp" : $"{issuer}/mcp/{path}";
                return Results.Json(new
                {
                    resource,
                    authorization_servers = new[] { issuer },
                    bearer_methods_supported = BearerMethods,
                });
            });
        return app;
    }
}
