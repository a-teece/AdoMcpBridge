using System.Text.Json;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Core.Abstractions;

namespace AdoMcpBridge.Api.Middleware;

internal sealed class BearerAuthenticationMiddleware
{
    private const string BearerPrefix = "Bearer ";
    private readonly RequestDelegate _next;
    private readonly ILogger<BearerAuthenticationMiddleware> _logger;

    public BearerAuthenticationMiddleware(RequestDelegate next, ILogger<BearerAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITokenStore store, IClock clock)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            await WriteChallenge(context, errorCode: null);
            return;
        }

        var token = auth[BearerPrefix.Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            await WriteChallenge(context, "invalid_token");
            return;
        }

        var hash = TokenHasher.Sha256Hex(token);
        var record = await store.FindByAccessTokenHashAsync(hash, context.RequestAborted);
        if (record is null)
        {
            _logger.LogInformation("Bearer rejected: unknown token hash");
            await WriteChallenge(context, "invalid_token");
            return;
        }

        if (record.AccessTokenExpiresAt <= clock.UtcNow)
        {
            _logger.LogInformation("Bearer rejected: expired");
            await WriteChallenge(context, "invalid_token");
            return;
        }

        context.Items[HttpContextItemKeys.TokenRecord] = record;
        await _next(context);
    }

    private static async Task WriteChallenge(HttpContext context, string? errorCode)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers["WWW-Authenticate"] = errorCode is null
            ? "Bearer realm=\"ado-mcp-bridge\""
            : $"Bearer realm=\"ado-mcp-bridge\", error=\"{errorCode}\"";
        context.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new { error = errorCode ?? "unauthorized" });
        await context.Response.WriteAsync(body);
    }
}
