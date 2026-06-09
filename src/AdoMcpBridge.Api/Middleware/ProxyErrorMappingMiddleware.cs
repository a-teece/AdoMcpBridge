using System.Text.Json;
using Yarp.ReverseProxy.Forwarder;

namespace AdoMcpBridge.Api.Middleware;

internal sealed class ProxyErrorMappingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ProxyErrorMappingMiddleware> _logger;

    public ProxyErrorMappingMiddleware(RequestDelegate next, ILogger<ProxyErrorMappingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        var err = context.Features.Get<IForwarderErrorFeature>();
        if (err is null) return;

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Upstream error {Error} but response already started", err.Error);
            return;
        }

        _logger.LogWarning("Upstream error {Error}", err.Error);
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new { error = "upstream_unavailable", category = err.Error.ToString() });
        await context.Response.WriteAsync(payload);
    }
}
