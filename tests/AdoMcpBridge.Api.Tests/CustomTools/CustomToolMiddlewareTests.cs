using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Core.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public sealed class CustomToolMiddlewareTests
{
    private sealed class CallbackTool(
        string name, Func<JsonElement, CancellationToken, Task<McpToolResult>> onInvoke) : ICustomMcpTool
    {
        public string Name => name;
        public string Description => "test tool";
        public object InputSchema => new { type = "object" };

        public Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
            => onInvoke(arguments, ct);
    }

    private static TokenRecord Record() => new(
        AccessTokenHash: "ah",
        RefreshTokenHash: "rh",
        ClientId: "cid",
        EntraRefreshTokenEncrypted: Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        UserObjectId: "oid",
        UserPrincipalName: "u@example.com",
        AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
        RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(14),
        CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

    private static DefaultHttpContext ContextForToolCall(string toolName)
    {
        var body =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"," +
            "\"params\":{\"name\":\"" + toolName + "\",\"arguments\":{}}}";
        var bytes = Encoding.UTF8.GetBytes(body);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.ContentType = "application/json";
        ctx.Request.ContentLength = bytes.Length;
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Response.Body = new MemoryStream();
        ctx.Items[HttpContextItemKeys.TokenRecord] = Record();
        return ctx;
    }

    private static IKeyVaultEncryptor Encryptor()
    {
        var encryptor = Substitute.For<IKeyVaultEncryptor>();
        encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<byte[]>(Encoding.UTF8.GetBytes("entra-refresh")));
        return encryptor;
    }

    [Fact]
    public async Task Populates_ado_rest_token_on_http_context_before_invoking_the_tool()
    {
        var ctx = ContextForToolCall("spy_tool");

        var entra = Substitute.For<IEntraTokenClient>();
        entra.AcquireAdoRestTokenAsync("entra-refresh", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(new EntraTokenResult(
                AccessToken: "ado-rest-token",
                RefreshToken: "new-refresh",
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(50),
                UserObjectId: "oid",
                UserPrincipalName: "u@example.com")));

        string? tokenSeenByTool = null;
        var tool = new CallbackTool("spy_tool", (_, _) =>
        {
            tokenSeenByTool = ctx.Items[HttpContextItemKeys.AdoRestAccessToken] as string;
            return Task.FromResult(new McpToolResult("ok"));
        });

        var mw = new CustomToolMiddleware(
            _ => Task.CompletedTask, new[] { (ICustomMcpTool)tool },
            NullLogger<CustomToolMiddleware>.Instance);

        await mw.InvokeAsync(ctx, Encryptor(), entra);

        tokenSeenByTool.Should().Be("ado-rest-token");
        ctx.Items[HttpContextItemKeys.AdoRestAccessToken].Should().Be("ado-rest-token");
    }

    [Fact]
    [Trait("category", "security")]
    public async Task Writes_jsonrpc_error_and_skips_tool_when_ado_swap_fails()
    {
        var ctx = ContextForToolCall("spy_tool");

        var entra = Substitute.For<IEntraTokenClient>();
        entra.AcquireAdoRestTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<EntraTokenResult>>(_ => throw new InvalidOperationException("swap boom"));

        var toolInvoked = false;
        var tool = new CallbackTool("spy_tool", (_, _) =>
        {
            toolInvoked = true;
            return Task.FromResult(new McpToolResult("ok"));
        });

        var mw = new CustomToolMiddleware(
            _ => Task.CompletedTask, new[] { (ICustomMcpTool)tool },
            NullLogger<CustomToolMiddleware>.Instance);

        await mw.InvokeAsync(ctx, Encryptor(), entra);

        toolInvoked.Should().BeFalse();

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        responseText.Should().NotContain("swap boom");

        using var doc = JsonDocument.Parse(responseText);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().Should().Be(-32000);
        error.GetProperty("message").GetString().Should().Be("ADO authentication failed");
    }
}
