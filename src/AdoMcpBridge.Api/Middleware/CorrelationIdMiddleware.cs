using System.Diagnostics;
using AdoMcpBridge.Api.Proxy;

namespace AdoMcpBridge.Api.Middleware;

internal sealed class CorrelationIdMiddleware
{
    private const string TraceParent = "traceparent";
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string traceId;
        if (context.Request.Headers.TryGetValue(TraceParent, out var inbound)
            && ActivityContext.TryParse(inbound.ToString(), null, out var parsed))
        {
            traceId = parsed.TraceId.ToString();
        }
        else
        {
            traceId = ActivityTraceId.CreateRandom().ToString();
        }

        context.Items[HttpContextItemKeys.CorrelationId] = traceId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader] = traceId;
            return Task.CompletedTask;
        });
        context.Response.Headers[CorrelationHeader] = traceId;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = traceId,
        });
        await _next(context);
    }
}
