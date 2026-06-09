namespace AdoMcpBridge.Core.Abstractions;

public sealed record AuthorizationCodeRecord(
    string Code,
    string ClientId,
    string RedirectUri,
    string PkceChallenge,
    string PkceMethod,
    string EntraRefreshTokenEncrypted,
    string UserObjectId,
    string UserPrincipalName,
    DateTimeOffset ExpiresAt);
