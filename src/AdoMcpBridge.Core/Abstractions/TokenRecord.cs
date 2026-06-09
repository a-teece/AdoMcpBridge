namespace AdoMcpBridge.Core.Abstractions;

public sealed record TokenRecord(
    string AccessTokenHash,
    string RefreshTokenHash,
    string ClientId,
    string EntraRefreshTokenEncrypted,
    string UserObjectId,
    string UserPrincipalName,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    DateTimeOffset CreatedAt);
