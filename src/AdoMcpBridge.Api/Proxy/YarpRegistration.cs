using AdoMcpBridge.Api.Middleware;

namespace AdoMcpBridge.Api.Proxy;

internal static class YarpRegistration
{
    public static IServiceCollection AddMcpProxy(this IServiceCollection services, IConfiguration config)
    {
        services.AddReverseProxy().LoadFromConfig(config.GetSection("ReverseProxy"));
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
            branch.UseMiddleware<HeaderPassthroughMiddleware>();
            branch.UseRouting();
            branch.UseEndpoints(endpoints => endpoints.MapReverseProxy());
        });

        return app;
    }
}
