namespace AdoMcpBridge.Core.Abstractions;

public interface IEntraTokenClient
{
    ValueTask<EntraTokenResult> ExchangeAuthorizationCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct);

    ValueTask<EntraTokenResult> AcquireAdoTokenAsync(
        string entraRefreshToken, CancellationToken ct);

    /// <summary>
    /// Redeems the stored Entra refresh token a second time for a token audienced
    /// for the classic Azure DevOps REST API (the native bridge tools' target),
    /// requesting <c>EntraOptions.AdoRestScopes</c> rather than the MCP-server
    /// scopes that <see cref="AcquireAdoTokenAsync"/> uses.
    /// </summary>
    ValueTask<EntraTokenResult> AcquireAdoRestTokenAsync(
        string entraRefreshToken, CancellationToken ct);
}
