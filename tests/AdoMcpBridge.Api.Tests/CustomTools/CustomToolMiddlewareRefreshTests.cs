using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Core.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

/// <summary>
/// Covers the self-healing tools/list_changed refresh: initialize interception
/// (listChanged capability + session birth) and stale-session notification injection.
/// </summary>
public sealed class CustomToolMiddlewareRefreshTests
{
    private const string SessionHeader = "Mcp-Session-Id";

    private sealed class CallbackTool(string name) : ICustomMcpTool
    {
        public string Name => name;
        public string Description => "test tool";
        public object InputSchema => new { type = "object" };

        public Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
            => Task.FromResult(new McpToolResult("ok"));
    }

    private static DefaultHttpContext Context(string bodyJson, string? requestSessionId = null)
    {
        var bytes = Encoding.UTF8.GetBytes(bodyJson);
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.ContentType = "application/json";
        ctx.Request.ContentLength = bytes.Length;
        ctx.Request.Body = new MemoryStream(bytes);
        if (requestSessionId is not null)
        {
            ctx.Request.Headers[SessionHeader] = requestSessionId;
        }
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static CustomToolMiddleware Middleware(
        RequestDelegate next, IMcpSessionRegistry registry, params ICustomMcpTool[] tools)
        => new(next, tools, registry, NullLogger<CustomToolMiddleware>.Instance);

    private static async Task<string> ReadResponseAsync(HttpContext ctx)
    {
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    }

    private static RequestDelegate WritesBody(string body, int status = 200, string? responseSessionId = null)
        => c =>
        {
            c.Response.StatusCode = status;
            if (responseSessionId is not null)
            {
                c.Response.Headers[SessionHeader] = responseSessionId;
            }
            return c.Response.WriteAsync(body);
        };

    private static async Task InvokeAsync(CustomToolMiddleware mw, HttpContext ctx)
        => await mw.InvokeAsync(ctx, Substitute.For<IKeyVaultEncryptor>(), Substitute.For<IEntraTokenClient>());

    private const string InitBody =
        "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2024-11-05\"," +
        "\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"ado\"}}}";

    private const string InitRequest = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}";

    [Fact]
    public async Task Initialize_json_response_gets_listChanged_true()
    {
        var ctx = Context(InitRequest);
        var mw = Middleware(WritesBody(InitBody), new McpSessionRegistry());

        await InvokeAsync(mw, ctx);

        using var doc = JsonDocument.Parse(await ReadResponseAsync(ctx));
        doc.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_sse_response_gets_listChanged_true()
    {
        var sse = "event: message\ndata: " + InitBody + "\n\n";
        var ctx = Context(InitRequest);
        var mw = Middleware(WritesBody(sse), new McpSessionRegistry());

        await InvokeAsync(mw, ctx);

        var text = await ReadResponseAsync(ctx);
        var dataLine = text.Split('\n').First(l => l.StartsWith("data:", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(dataLine["data:".Length..].Trim());
        doc.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_creates_capabilities_and_tools_objects_when_absent()
    {
        const string noCaps = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2024-11-05\"}}";
        var ctx = Context(InitRequest);
        var mw = Middleware(WritesBody(noCaps), new McpSessionRegistry());

        await InvokeAsync(mw, ctx);

        using var doc = JsonDocument.Parse(await ReadResponseAsync(ctx));
        doc.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_response_session_header_marks_the_session_born()
    {
        var registry = new McpSessionRegistry();
        var ctx = Context(InitRequest);
        var mw = Middleware(WritesBody(InitBody, responseSessionId: "born-session-abcdef"), registry);

        await InvokeAsync(mw, ctx);

        // Born here → never treated as stale.
        registry.ShouldNotify("born-session-abcdef").Should().BeFalse();
    }

    [Fact]
    public async Task Initialize_without_response_session_header_still_patches_but_records_nothing()
    {
        var registry = new McpSessionRegistry();
        var ctx = Context(InitRequest);
        var mw = Middleware(WritesBody(InitBody), registry);

        await InvokeAsync(mw, ctx);

        using var doc = JsonDocument.Parse(await ReadResponseAsync(ctx));
        doc.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
        // No id recorded, so an unrelated session is still notifiable.
        registry.ShouldNotify("some-other-session").Should().BeTrue();
    }

    private const string SseToolCallResponse =
        "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[]}}\n\n";

    private const string ToolCallRequest =
        "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"upstream_tool\",\"arguments\":{}}}";

    [Fact]
    public async Task Stale_session_with_sse_response_gets_notification_frame_before_the_response_frame()
    {
        var registry = new McpSessionRegistry();
        var ctx = Context(ToolCallRequest, requestSessionId: "stale-session-123");
        var mw = Middleware(WritesBody(SseToolCallResponse), registry);

        await InvokeAsync(mw, ctx);

        var text = await ReadResponseAsync(ctx);
        text.Should().Contain("notifications/tools/list_changed");
        // Notification frame must precede the response's own frame.
        text.IndexOf("list_changed", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("\"result\"", StringComparison.Ordinal));
        // Session consumed exactly once.
        registry.ShouldNotify("stale-session-123").Should().BeFalse();
    }

    [Fact]
    public async Task Stale_session_is_not_notified_twice()
    {
        var registry = new McpSessionRegistry();
        var mw = Middleware(WritesBody(SseToolCallResponse), registry);

        var first = Context(ToolCallRequest, requestSessionId: "stale-twice");
        await InvokeAsync(mw, first);
        (await ReadResponseAsync(first)).Should().Contain("list_changed");

        var second = Context(ToolCallRequest, requestSessionId: "stale-twice");
        await InvokeAsync(mw, second);
        (await ReadResponseAsync(second)).Should().NotContain("list_changed");
    }

    [Fact]
    public async Task Stale_session_with_plain_json_response_is_left_untouched_and_not_marked_notified()
    {
        const string json = "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[]}}";
        var registry = new McpSessionRegistry();
        var ctx = Context(ToolCallRequest, requestSessionId: "stale-json");
        var mw = Middleware(WritesBody(json), registry);

        await InvokeAsync(mw, ctx);

        (await ReadResponseAsync(ctx)).Should().Be(json).And.NotContain("list_changed");
        // Not consumed — the next SSE response can still carry the hint.
        registry.ShouldNotify("stale-json").Should().BeTrue();
    }

    [Fact]
    public async Task Non_success_sse_response_is_not_injected()
    {
        var registry = new McpSessionRegistry();
        var ctx = Context(ToolCallRequest, requestSessionId: "stale-500");
        var mw = Middleware(WritesBody(SseToolCallResponse, status: 500), registry);

        await InvokeAsync(mw, ctx);

        (await ReadResponseAsync(ctx)).Should().NotContain("list_changed");
        registry.ShouldNotify("stale-500").Should().BeTrue();
    }

    [Fact]
    public async Task Session_born_in_process_is_never_notified()
    {
        var registry = new McpSessionRegistry();
        registry.MarkBorn("home-session");
        var ctx = Context(ToolCallRequest, requestSessionId: "home-session");
        var mw = Middleware(WritesBody(SseToolCallResponse), registry);

        await InvokeAsync(mw, ctx);

        (await ReadResponseAsync(ctx)).Should().NotContain("list_changed");
    }

    [Fact]
    public async Task Request_without_session_header_is_never_notified()
    {
        var registry = new McpSessionRegistry();
        var ctx = Context(ToolCallRequest);
        var mw = Middleware(WritesBody(SseToolCallResponse), registry);

        await InvokeAsync(mw, ctx);

        (await ReadResponseAsync(ctx)).Should().NotContain("list_changed");
    }

    [Fact]
    public async Task Notification_only_post_is_passed_through_untouched()
    {
        const string notification = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
        var registry = new McpSessionRegistry();
        var ctx = Context(notification, requestSessionId: "stale-notif");
        var nextCalled = false;
        var mw = Middleware(
            c => { nextCalled = true; c.Response.StatusCode = 202; return Task.CompletedTask; },
            registry);

        await InvokeAsync(mw, ctx);

        nextCalled.Should().BeTrue();
        // A notification-only post has no id, so the session is never consumed.
        registry.ShouldNotify("stale-notif").Should().BeTrue();
    }

    private const string SseToolsListResponse =
        "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{\"tools\":[" +
        "{\"name\":\"upstream_tool\",\"description\":\"d\",\"inputSchema\":{\"type\":\"object\"}}]}}\n\n";

    private const string ToolsListRequest = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}";

    [Fact]
    public async Task Stale_session_calling_tools_list_gets_both_native_tool_injection_and_notification()
    {
        var registry = new McpSessionRegistry();
        var ctx = Context(ToolsListRequest, requestSessionId: "stale-list");
        var mw = Middleware(WritesBody(SseToolsListResponse), registry, new CallbackTool("native_custom_tool"));

        await InvokeAsync(mw, ctx);

        var text = await ReadResponseAsync(ctx);
        text.Should().Contain("notifications/tools/list_changed");
        text.Should().Contain("native_custom_tool");
        registry.ShouldNotify("stale-list").Should().BeFalse();
    }

    [Fact]
    public async Task Non_stale_session_tools_list_gets_native_tools_but_no_notification()
    {
        var registry = new McpSessionRegistry();
        registry.MarkBorn("fresh-list");
        var ctx = Context(ToolsListRequest, requestSessionId: "fresh-list");
        var mw = Middleware(WritesBody(SseToolsListResponse), registry, new CallbackTool("native_custom_tool"));

        await InvokeAsync(mw, ctx);

        var text = await ReadResponseAsync(ctx);
        text.Should().Contain("native_custom_tool");
        text.Should().NotContain("list_changed");
    }
}
