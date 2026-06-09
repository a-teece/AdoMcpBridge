namespace AdoMcpBridge.Core.Abstractions;

public interface ITokenStore
{
    ValueTask<RegisteredClient?> FindClientAsync(string clientId, CancellationToken ct);
    ValueTask AddClientAsync(RegisteredClient client, CancellationToken ct);

    ValueTask AddAuthorizationCodeAsync(AuthorizationCodeRecord code, CancellationToken ct);
    ValueTask<AuthorizationCodeRecord?> ConsumeAuthorizationCodeAsync(string code, CancellationToken ct);

    ValueTask AddTokenAsync(TokenRecord token, CancellationToken ct);
    ValueTask<TokenRecord?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct);
    ValueTask<TokenRecord?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct);
    ValueTask RevokeTokenAsync(string refreshTokenHash, CancellationToken ct);
    ValueTask ReplaceTokenAsync(TokenRecord oldToken, TokenRecord newToken, CancellationToken ct);
}
