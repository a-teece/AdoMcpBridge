using System.Text.Json;
using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Core.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdoMcpBridge.Api.Tests.Middleware;

public class ErrorHandlingMiddlewareTests
{
    private static async Task<(int status, string body, string? corr)> Invoke(
        string path, RequestDelegate next)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        var mw = new ErrorHandlingMiddleware(next, NullLogger<ErrorHandlingMiddleware>.Instance);
        await mw.InvokeAsync(ctx);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        ctx.Response.Headers.TryGetValue("X-Correlation-Id", out var corr);
        return (ctx.Response.StatusCode, body, corr);
    }

    [Fact]
    public async Task CallerError_On_Token_Endpoint_Returns_Rfc6749_Json()
    {
        var (status, body, _) = await Invoke("/token", _ =>
            throw new CallerErrorException("invalid_grant", "code expired"));

        status.Should().Be(400);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
        doc.RootElement.GetProperty("error_description").GetString().Should().Be("code expired");
    }

    [Fact]
    public async Task UpstreamError_Returns_ProblemJson_With_Mapped_Status()
    {
        var (status, body, corr) = await Invoke("/mcp/foo", _ =>
            throw new UpstreamErrorException("rate limited", mappedStatusCode: 429));

        status.Should().Be(429);
        corr.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Be("upstream_error");
        doc.RootElement.GetProperty("correlation_id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InternalError_Returns_Opaque_500_With_ErrorId()
    {
        var (status, body, _) = await Invoke("/mcp/foo", _ =>
            throw new InternalErrorException("secret detail must not leak"));

        status.Should().Be(500);
        body.Should().NotContain("secret detail must not leak");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error_id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Unhandled_Exception_Is_Wrapped_As_Internal()
    {
        var (status, body, _) = await Invoke("/mcp/foo", _ => throw new InvalidOperationException("boom"));

        status.Should().Be(500);
        body.Should().NotContain("boom");
    }

    [Fact]
    public async Task CallerError_On_Non_OAuth_Path_Uses_ProblemDetails()
    {
        var (status, body, _) = await Invoke("/mcp/foo", _ =>
            throw new CallerErrorException("bad_request", "missing header"));

        status.Should().Be(400);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("error", out _).Should().BeFalse();
        doc.RootElement.GetProperty("title").GetString().Should().Be("bad_request");
    }

    [Fact]
    public async Task Success_Path_Passes_Through_And_Stamps_Correlation_Id()
    {
        var (status, _, corr) = await Invoke("/healthz", ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        status.Should().Be(200);
        corr.Should().NotBeNullOrEmpty();
    }
}
