using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.Middleware;
using Yarp.ReverseProxy.Transforms;

namespace AdoMcpBridge.Api.Proxy;

internal static class YarpRegistration
{
    public static IServiceCollection AddMcpProxy(this IServiceCollection services, IConfiguration config)
    {
        services.AddReverseProxy()
            .LoadFromConfig(config.GetSection("ReverseProxy"))
            .AddTransforms(builderCtx =>
            {
                // The header allowlist is authoritative: do not let YARP inject its
                // own X-Forwarded-* headers, which would otherwise reach upstream.
                builderCtx.UseDefaultForwarders = false;
                builderCtx.AddResponseTransform(transformCtx =>
                    new UpstreamErrorTransform().ApplyAsync(transformCtx));
            });
        return services;
    }

    public static WebApplication UseMcpProxy(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/mcp"), branch =>
        {
            branch.UseMiddleware<ProxyErrorMappingMiddleware>();
            branch.UseMiddleware<BearerAuthenticationMiddleware>();
            branch.UseMiddleware<EntraTokenSwapMiddleware>();
            branch.UseMiddleware<CustomToolMiddleware>();
            branch.UseMiddleware<HeaderPassthroughMiddleware>();
            branch.UseRouting();
            branch.UseEndpoints(endpoints => endpoints.MapReverseProxy());
        });

        return app;
    }
}
