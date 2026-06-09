using AdoMcpBridge.Api.Options;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Api.Endpoints;

public static class EntraCallbackEndpoint
{
    public static IEndpointRouteBuilder MapEntraCallback(this IEndpointRouteBuilder app)
    {
        app.MapGet("/authorize/callback", async (
            [FromQuery(Name = "code")] string code,
            [FromQuery(Name = "state")] string state,
            [FromQuery(Name = "session_id")] string sessionId,
            IAuthorizationSessionCache cache,
            IEntraTokenClient entra,
            ITokenStore store,
            IKeyVaultEncryptor encryptor,
            WrapperTokenMinter minter,
            IClock clock,
            IOptions<AdoMcpOptions> opts,
            CancellationToken ct) =>
        {
            var s = await cache.GetAsync(sessionId, ct);
            if (s is null)
            {
                return Results.BadRequest(System.Text.Json.JsonDocument.Parse(
                    OAuthError.InvalidRequest("session expired").ToJson()).RootElement);
            }

            if (!string.Equals(state, s.EntraState, StringComparison.Ordinal))
            {
                return Results.BadRequest(System.Text.Json.JsonDocument.Parse(
                    OAuthError.InvalidRequest("state mismatch").ToJson()).RootElement);
            }

            var bridgeCallback = $"{opts.Value.Issuer.TrimEnd('/')}/authorize/callback";
            var result = await entra.ExchangeAuthorizationCodeAsync(
                code, s.EntraCodeVerifier, bridgeCallback, ct);

            var encryptedRt = await encryptor.EncryptAsync(
                System.Text.Encoding.UTF8.GetBytes(result.RefreshToken), ct);

            var authCode = minter.MintOpaque();
            await store.AddAuthorizationCodeAsync(new AuthorizationCodeRecord(
                Code: authCode,
                ClientId: s.ClientId,
                RedirectUri: s.RedirectUri,
                PkceChallenge: s.ClientCodeChallenge,
                PkceMethod: s.ClientCodeChallengeMethod,
                EntraRefreshTokenEncrypted: Convert.ToBase64String(encryptedRt),
                UserObjectId: result.UserObjectId,
                UserPrincipalName: result.UserPrincipalName,
                ExpiresAt: clock.UtcNow.AddSeconds(60)), ct);

            await cache.RemoveAsync(sessionId, ct);

            var redirect = $"{s.RedirectUri}?code={Uri.EscapeDataString(authCode)}" +
                           $"&state={Uri.EscapeDataString(s.ClientState)}";
            return Results.Redirect(redirect);
        });
        return app;
    }
}
