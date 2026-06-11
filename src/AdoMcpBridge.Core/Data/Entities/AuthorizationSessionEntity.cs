namespace AdoMcpBridge.Core.Data.Entities;

internal sealed class AuthorizationSessionEntity
{
    public string SessionId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string ClientCodeChallenge { get; set; } = string.Empty;
    public string ClientCodeChallengeMethod { get; set; } = string.Empty;
    public string ClientState { get; set; } = string.Empty;
    public string EntraCodeVerifier { get; set; } = string.Empty;
    public string EntraState { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
