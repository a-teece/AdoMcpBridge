using AdoMcpBridge.Api.Options;
using AdoMcpBridge.Core.OAuth;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Api.Endpoints;

public static class MetadataEndpoint
{
    public static IEndpointRouteBuilder MapMetadata(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-authorization-server",
            (IOptions<AdoMcpOptions> opts) =>
                Results.Json(AuthorizationServerMetadata.ForIssuer(opts.Value.Issuer)));
        return app;
    }
}
