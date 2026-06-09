using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;

namespace AdoMcpBridge.Api.Endpoints;

public static class RegisterEndpoint
{
    public static IEndpointRouteBuilder MapRegister(this IEndpointRouteBuilder app)
    {
        app.MapPost("/register", async (
            RegistrationRequest req,
            ITokenStore store,
            IClock clock,
            WrapperTokenMinter minter,
            CancellationToken ct) =>
        {
            if (req.RedirectUris is null || req.RedirectUris.Count == 0)
            {
                return Results.BadRequest(System.Text.Json.JsonDocument.Parse(
                    OAuthError.InvalidRequest("redirect_uris required").ToJson()).RootElement);
            }

            foreach (var u in req.RedirectUris)
            {
                if (!Uri.TryCreate(u, UriKind.Absolute, out var parsed) ||
                    !string.Equals(parsed.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(System.Text.Json.JsonDocument.Parse(
                        OAuthError.InvalidRequest("redirect_uri must be https").ToJson()).RootElement);
                }
            }

            var clientId = minter.MintOpaque();
            await store.AddClientAsync(
                new RegisteredClient(clientId, req.ClientName ?? "", req.RedirectUris.AsReadOnly(), clock.UtcNow), ct);

            return Results.Created($"/register/{clientId}", new RegistrationResponse
            {
                ClientId = clientId,
                ClientName = req.ClientName ?? "",
                RedirectUris = req.RedirectUris,
            });
        });
        return app;
    }
}
