using System.Text;
using System.Text.Json;
using Yarp.ReverseProxy.Transforms;

namespace AdoMcpBridge.Api.Proxy;

internal sealed class UpstreamErrorTransform : ResponseTransform
{
    public override async ValueTask ApplyAsync(ResponseTransformContext context)
    {
        var status = context.ProxyResponse?.StatusCode;
        if (status is null) return;
        if ((int)status < 500) return;

        context.SuppressResponseBody = true;
        context.HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
        context.HttpContext.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            error = "upstream_unavailable",
            upstream_status = (int)status,
        });
        var bytes = Encoding.UTF8.GetBytes(payload);
        await context.HttpContext.Response.Body.WriteAsync(bytes);
    }
}
