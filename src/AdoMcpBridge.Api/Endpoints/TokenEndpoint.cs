using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;

namespace AdoMcpBridge.Api.Endpoints;

public static class TokenEndpoint
{
    public static IEndpointRouteBuilder MapToken(this IEndpointRouteBuilder app)
    {
        app.MapPost("/token", async (
            HttpRequest req,
            ITokenStore store,
            PkceValidator pkce,
            WrapperTokenMinter minter,
            IClock clock,
            CancellationToken ct) =>
        {
            if (!req.HasFormContentType)
                return BadRequest(OAuthError.InvalidRequest("expected form-encoded body"));
            var form = await req.ReadFormAsync(ct);

            var grantType = form["grant_type"].ToString();
            return grantType switch
            {
                "authorization_code" => await AuthCodeAsync(form, store, pkce, minter, clock, ct),
                "refresh_token" => await RefreshAsync(form, store, minter, clock, ct),
                _ => BadRequest(OAuthError.UnsupportedGrantType($"grant_type '{grantType}' not supported")),
            };
        }).DisableAntiforgery();
        return app;
    }

    private static IResult BadRequest(OAuthError err) =>
        Results.Content(err.ToJson(), "application/json", statusCode: 400);

    private static async Task<IResult> AuthCodeAsync(
        IFormCollection form, ITokenStore store, PkceValidator pkce,
        WrapperTokenMinter minter, IClock clock, CancellationToken ct)
    {
        var code = form["code"].ToString();
        var clientId = form["client_id"].ToString();
        var redirectUri = form["redirect_uri"].ToString();
        var verifier = form["code_verifier"].ToString();

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(verifier))
            return BadRequest(OAuthError.InvalidRequest("code and code_verifier required"));

        var record = await store.ConsumeAuthorizationCodeAsync(code, ct);
        if (record is null) return BadRequest(OAuthError.InvalidGrant("unknown or used code"));
        if (record.ExpiresAt <= clock.UtcNow) return BadRequest(OAuthError.InvalidGrant("code expired"));
        if (record.ClientId != clientId) return BadRequest(OAuthError.InvalidGrant("client mismatch"));
        if (record.RedirectUri != redirectUri) return BadRequest(OAuthError.InvalidGrant("redirect_uri mismatch"));
        if (!pkce.Verify(verifier, record.PkceChallenge, record.PkceMethod))
            return BadRequest(OAuthError.InvalidGrant("PKCE verification failed"));

        var pair = minter.MintPair();
        var rec = new TokenRecord(
            AccessTokenHash: minter.Hash(pair.AccessToken),
            RefreshTokenHash: minter.Hash(pair.RefreshToken),
            ClientId: clientId,
            EntraRefreshTokenEncrypted: record.EntraRefreshTokenEncrypted,
            UserObjectId: record.UserObjectId,
            UserPrincipalName: record.UserPrincipalName,
            AccessTokenExpiresAt: minter.AccessTokenExpiresAt,
            RefreshTokenExpiresAt: minter.RefreshTokenExpiresAt,
            CreatedAt: clock.UtcNow);
        await store.AddTokenAsync(rec, ct);

        return Results.Json(new TokenResponse
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            ExpiresIn = 3600,
        });
    }

    private static Task<IResult> RefreshAsync(
        IFormCollection form, ITokenStore store, WrapperTokenMinter minter, IClock clock, CancellationToken ct)
        => Task.FromResult(BadRequest(OAuthError.UnsupportedGrantType("refresh_token not yet wired")));
}
