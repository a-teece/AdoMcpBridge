using AdoMcpBridge.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace AdoMcpBridge.Api.Tests.Middleware;

public sealed class HeaderPassthroughMiddlewareTests
{
    [Fact]
    public async Task Keeps_allowlisted_headers()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-MCP-Toolsets"] = "repos,workitems";
        ctx.Request.Headers["X-MCP-Readonly"] = "true";
        ctx.Request.Headers["X-MCP-Tools"] = "list_repos";
        ctx.Request.Headers["X-MCP-Insiders"] = "true";

        var mw = new HeaderPassthroughMiddleware(_ => Task.CompletedTask);
        await mw.InvokeAsync(ctx);

        ctx.Request.Headers["X-MCP-Toolsets"].ToString().Should().Be("repos,workitems");
        ctx.Request.Headers["X-MCP-Readonly"].ToString().Should().Be("true");
        ctx.Request.Headers["X-MCP-Tools"].ToString().Should().Be("list_repos");
        ctx.Request.Headers["X-MCP-Insiders"].ToString().Should().Be("true");
    }

    [Fact]
    public async Task Strips_unknown_x_headers()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-MCP-Toolsets"] = "ok";
        ctx.Request.Headers["X-Forwarded-Host"] = "evil.example.com";
        ctx.Request.Headers["X-MCP-Secret"] = "leak";
        ctx.Request.Headers["X-Anything"] = "drop";

        var mw = new HeaderPassthroughMiddleware(_ => Task.CompletedTask);
        await mw.InvokeAsync(ctx);

        ctx.Request.Headers.Should().NotContainKey("X-Forwarded-Host");
        ctx.Request.Headers.Should().NotContainKey("X-MCP-Secret");
        ctx.Request.Headers.Should().NotContainKey("X-Anything");
        ctx.Request.Headers["X-MCP-Toolsets"].ToString().Should().Be("ok");
    }

    [Fact]
    public async Task Does_not_strip_standard_non_x_headers()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Authorization"] = "Bearer ado";
        ctx.Request.Headers["Content-Type"] = "application/json";
        ctx.Request.Headers["Accept"] = "*/*";

        var mw = new HeaderPassthroughMiddleware(_ => Task.CompletedTask);
        await mw.InvokeAsync(ctx);

        ctx.Request.Headers["Authorization"].ToString().Should().Be("Bearer ado");
        ctx.Request.Headers["Content-Type"].ToString().Should().Be("application/json");
        ctx.Request.Headers["Accept"].ToString().Should().Be("*/*");
    }
}
