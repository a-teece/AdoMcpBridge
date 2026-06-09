namespace AdoMcpBridge.Core.Abstractions;

public sealed record EntraTokenResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string UserObjectId,
    string UserPrincipalName);
