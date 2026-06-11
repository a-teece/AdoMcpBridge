using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Api.Options;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Transforms;

namespace AdoMcpBridge.Api.Proxy;

internal sealed class UpstreamErrorTransform : ResponseTransform
{
    public override async ValueTask ApplyAsync(ResponseTransformContext context)
    {
        var status = context.ProxyResponse?.StatusCode;
        if (status is null) return;
        if ((int)status < 400) return;

        if ((int)status < 500)
        {
            // Never let the upstream's WWW-Authenticate reach the
            // client: it names mcp.dev.azure.com as the protected
            // resource, which wedges spec-compliant MCP clients on a
            // resource-mismatch error (issue #40). On 401, present the
            // bridge's own challenge instead.
            var http = context.HttpContext;
            http.Response.Headers.Remove("WWW-Authenticate");
            if (status == System.Net.HttpStatusCode.Unauthorized)
            {
                var issuer = http.RequestServices
                    .GetRequiredService<IOptions<AdoMcpOptions>>().Value.Issuer;
                http.Response.Headers["WWW-Authenticate"] =
                    BridgeChallenge.For(issuer, http.Request.Path, "invalid_token");
            }
            return;
        }

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
