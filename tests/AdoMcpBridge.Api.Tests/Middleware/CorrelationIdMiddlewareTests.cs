using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Api.Proxy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdoMcpBridge.Api.Tests.Middleware;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Generates_traceparent_when_missing_and_stamps_response_header()
    {
        var ctx = new DefaultHttpContext();
        var mw = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        ctx.Response.Headers.Should().ContainKey("X-Correlation-Id");
        ctx.Response.Headers["X-Correlation-Id"].ToString().Should().NotBeNullOrWhiteSpace();
        ctx.Items[HttpContextItemKeys.CorrelationId].Should().NotBeNull();
    }

    [Fact]
    public async Task Honors_inbound_traceparent()
    {
        const string inbound = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["traceparent"] = inbound;

        var mw = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);
        await mw.InvokeAsync(ctx);

        ctx.Response.Headers["X-Correlation-Id"].ToString()
            .Should().Be("0af7651916cd43dd8448eb211c80319c");
    }
}
