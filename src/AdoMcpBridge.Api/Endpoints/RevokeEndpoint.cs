using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;

namespace AdoMcpBridge.Api.Endpoints;

public static class RevokeEndpoint
{
    public static IEndpointRouteBuilder MapRevoke(this IEndpointRouteBuilder app)
    {
        app.MapPost("/revoke", async (
            HttpRequest req, ITokenStore store, WrapperTokenMinter minter, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.Ok();
            var form = await req.ReadFormAsync(ct);
            var token = form["token"].ToString();
            if (string.IsNullOrEmpty(token)) return Results.Ok();
            await store.RevokeTokenAsync(minter.Hash(token), ct);
            return Results.Ok();
        }).DisableAntiforgery();
        return app;
    }
}
