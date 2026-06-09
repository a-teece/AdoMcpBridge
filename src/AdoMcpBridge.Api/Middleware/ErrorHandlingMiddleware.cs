using System.Diagnostics;
using System.Text.Json;
using AdoMcpBridge.Core.Errors;
using Microsoft.AspNetCore.Mvc;

namespace AdoMcpBridge.Api.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private static readonly string[] OAuthPaths =
        { "/token", "/authorize", "/register", "/revoke" };

    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _log;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var correlationId = Activity.Current?.TraceId.ToString()
                            ?? Guid.NewGuid().ToString("n");
        ctx.Response.Headers["X-Correlation-Id"] = correlationId;

        try
        {
            await _next(ctx);
        }
        catch (CallerErrorException ex)
        {
            _log.LogInformation("caller error {ErrorCode} on {Path}", ex.ErrorCode, ctx.Request.Path);
            if (IsOAuthPath(ctx.Request.Path))
            {
                await WriteAsync(ctx, ex.StatusCode, new
                {
                    error = ex.ErrorCode,
                    error_description = ex.Message,
                    correlation_id = correlationId,
                });
            }
            else
            {
                await WriteAsync(ctx, ex.StatusCode, new ProblemDetails
                {
                    Status = ex.StatusCode,
                    Title = ex.ErrorCode,
                    Detail = ex.Message,
                    Extensions = { ["correlation_id"] = correlationId },
                });
            }
        }
        catch (UpstreamErrorException ex)
        {
            _log.LogWarning(ex, "upstream error on {Path}", ctx.Request.Path);
            await WriteProblem(ctx, ex.StatusCode, "upstream_error", ex.Message, correlationId, ex.ErrorId);
        }
        catch (InternalErrorException ex)
        {
            _log.LogError(ex, "internal error {ErrorId} on {Path}", ex.ErrorId, ctx.Request.Path);
            await WriteProblem(ctx, ex.StatusCode, "internal_error",
                "An internal error occurred.", correlationId, ex.ErrorId);
        }
        catch (Exception ex)
        {
            var wrapped = new InternalErrorException("unhandled", ex);
            _log.LogError(ex, "unhandled exception {ErrorId} on {Path}", wrapped.ErrorId, ctx.Request.Path);
            await WriteProblem(ctx, 500, "internal_error",
                "An internal error occurred.", correlationId, wrapped.ErrorId);
        }
    }

    private static bool IsOAuthPath(PathString path)
    {
        foreach (var p in OAuthPaths)
        {
            if (path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static async Task WriteAsync(HttpContext ctx, int status, object body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, body);
    }

    private static Task WriteProblem(HttpContext ctx, int status, string title,
        string detail, string correlationId, string errorId)
    {
        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Extensions =
            {
                ["correlation_id"] = correlationId,
                ["error_id"] = errorId,
            },
        };
        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.StatusCode = status;
        return JsonSerializer.SerializeAsync(ctx.Response.Body, pd);
    }
}
