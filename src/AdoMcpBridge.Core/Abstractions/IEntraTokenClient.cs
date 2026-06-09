namespace AdoMcpBridge.Core.Abstractions;

public interface IEntraTokenClient
{
    ValueTask<EntraTokenResult> ExchangeAuthorizationCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct);

    ValueTask<EntraTokenResult> AcquireAdoTokenAsync(
        string entraRefreshToken, CancellationToken ct);
}
