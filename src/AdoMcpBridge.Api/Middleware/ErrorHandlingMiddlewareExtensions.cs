namespace AdoMcpBridge.Api.Middleware;

public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseBridgeErrorHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ErrorHandlingMiddleware>();
}
