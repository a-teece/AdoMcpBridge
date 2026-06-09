namespace AdoMcpBridge.Core.OAuth;

public sealed record AuthorizationSession(
    string SessionId,
    string ClientId,
    string RedirectUri,
    string ClientCodeChallenge,
    string ClientCodeChallengeMethod,
    string ClientState,
    string EntraCodeVerifier,
    string EntraState,
    DateTimeOffset ExpiresAt);
