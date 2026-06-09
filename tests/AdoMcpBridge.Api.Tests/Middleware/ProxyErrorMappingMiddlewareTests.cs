using AdoMcpBridge.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Yarp.ReverseProxy.Forwarder;

namespace AdoMcpBridge.Api.Tests.Middleware;

public sealed class ProxyErrorMappingMiddlewareTests
{
    [Fact]
    public async Task Maps_yarp_request_error_to_502_translated_body()
    {
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Features.Set<IForwarderErrorFeature>(new TestForwarderError(ForwarderError.Request));

        var mw = new ProxyErrorMappingMiddleware(c =>
        {
            c.Response.StatusCode = 502;
            return Task.CompletedTask;
        }, NullLogger<ProxyErrorMappingMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(502);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain("upstream_unavailable");
    }

    [Fact]
    public async Task Leaves_2xx_response_untouched()
    {
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var mw = new ProxyErrorMappingMiddleware(c =>
        {
            c.Response.StatusCode = 200;
            return c.Response.WriteAsync("ok");
        }, NullLogger<ProxyErrorMappingMiddleware>.Instance);

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Be("ok");
    }

    private sealed class TestForwarderError : IForwarderErrorFeature
    {
        public TestForwarderError(ForwarderError error) => Error = error;
        public ForwarderError Error { get; }
        public Exception? Exception => null;
    }
}
