using System.Security.Cryptography;
using System.Text;
using AdoMcpBridge.Api.Options;
using AdoMcpBridge.Core.OAuth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Api.Endpoints;

public static class ConsentSubmitEndpoint
{
    public static IEndpointRouteBuilder MapConsentSubmit(this IEndpointRouteBuilder app)
    {
        app.MapPost("/authorize/consent", async (
            [FromForm] string session_id,
            [FromForm] string decision,
            IAuthorizationSessionCache cache,
            IOptions<AdoMcpOptions> opts,
            CancellationToken ct) =>
        {
            var s = await cache.GetAsync(session_id, ct);
            if (s is null)
            {
                return Results.BadRequest(System.Text.Json.JsonDocument.Parse(
                    OAuthError.InvalidRequest("session expired or unknown").ToJson()).RootElement);
            }

            if (decision != "approve")
            {
                await cache.RemoveAsync(session_id, ct);
                return Results.Redirect(
                    $"{s.RedirectUri}?error=access_denied&state={Uri.EscapeDataString(s.ClientState)}");
            }

            var entra = opts.Value.Entra;
            var challenge = BuildS256(s.EntraCodeVerifier);
            var bridgeCallback = $"{opts.Value.Issuer.TrimEnd('/')}/authorize/callback";
            var url = $"{entra.Authority.TrimEnd('/').Replace("/v2.0", "")}/oauth2/v2.0/authorize" +
                      $"?client_id={Uri.EscapeDataString(entra.ClientId)}" +
                      $"&response_type=code" +
                      $"&redirect_uri={Uri.EscapeDataString(bridgeCallback)}" +
                      $"&response_mode=query" +
                      $"&scope={Uri.EscapeDataString(string.Join(" ", entra.Scopes))}" +
                      $"&state={Uri.EscapeDataString(s.EntraState)}" +
                      $"&code_challenge={Uri.EscapeDataString(challenge)}" +
                      $"&code_challenge_method=S256";
            return Results.Redirect(url);
        }).DisableAntiforgery();
        return app;
    }

    private static string BuildS256(string verifier)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.ASCII.GetBytes(verifier), hash);
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
