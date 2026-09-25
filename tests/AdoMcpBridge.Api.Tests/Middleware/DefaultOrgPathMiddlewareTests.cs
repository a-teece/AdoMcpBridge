using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Api.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Api.Tests.Middleware;

public sealed class DefaultOrgPathMiddlewareTests
{
    private static IOptions<AdoMcpOptions> Options(string defaultOrganization)
        => Microsoft.Extensions.Options.Options.Create(
            new AdoMcpOptions { DefaultOrganization = defaultOrganization });

    private static async Task<(string path, bool nextCalled)> RunAsync(
        string requestPath, string defaultOrganization)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = requestPath;

        var nextCalled = false;
        var mw = new DefaultOrgPathMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; }, Options(defaultOrganization));

        await mw.InvokeAsync(ctx);

        return (ctx.Request.Path.Value!, nextCalled);
    }

    [Fact]
    public async Task Rewrites_bare_mcp_to_the_default_org()
    {
        var (path, nextCalled) = await RunAsync("/mcp", "Enate");

        path.Should().Be("/mcp/Enate");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Rewrites_bare_mcp_with_trailing_slash_to_the_default_org()
    {
        var (path, nextCalled) = await RunAsync("/mcp/", "Enate");

        path.Should().Be("/mcp/Enate");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Rewrites_bare_mcp_case_insensitively()
    {
        var (path, nextCalled) = await RunAsync("/MCP", "Enate");

        path.Should().Be("/mcp/Enate");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Leaves_a_path_with_an_org_segment_unchanged()
    {
        var (path, nextCalled) = await RunAsync("/mcp/Contoso", "Enate");

        path.Should().Be("/mcp/Contoso");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Leaves_bare_mcp_unchanged_when_no_default_is_configured()
    {
        var (path, nextCalled) = await RunAsync("/mcp", "");

        path.Should().Be("/mcp");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Leaves_bare_mcp_unchanged_when_default_is_whitespace_only()
    {
        // A whitespace-only configured org is treated as absent — consistent with the
        // native-tool path (CustomToolMiddleware.ApplyDefaultOrganization uses IsNullOrWhiteSpace).
        var (path, nextCalled) = await RunAsync("/mcp", "   ");

        path.Should().Be("/mcp");
        nextCalled.Should().BeTrue();
    }
}
