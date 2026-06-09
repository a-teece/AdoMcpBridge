using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Core.Abstractions;

namespace AdoMcpBridge.Api.Middleware;

internal sealed class EntraTokenSwapMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<EntraTokenSwapMiddleware> _logger;

    public EntraTokenSwapMiddleware(RequestDelegate next, ILogger<EntraTokenSwapMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IKeyVaultEncryptor encryptor, IEntraTokenClient entra)
    {
        if (context.Items[HttpContextItemKeys.TokenRecord] is not TokenRecord record)
        {
            _logger.LogError("EntraTokenSwap reached without TokenRecord — middleware order bug");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "internal_error" }));
            return;
        }

        string entraAccess;
        try
        {
            var cipher = Convert.FromBase64String(record.EntraRefreshTokenEncrypted);
            var plaintext = await encryptor.DecryptAsync(cipher, context.RequestAborted);
            var refreshToken = Encoding.UTF8.GetString(plaintext);
            var result = await entra.AcquireAdoTokenAsync(refreshToken, context.RequestAborted);
            entraAccess = result.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entra token acquisition failed");
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "entra_unavailable" }));
            return;
        }

        context.Request.Headers["Authorization"] = $"Bearer {entraAccess}";
        await _next(context);
    }
}
