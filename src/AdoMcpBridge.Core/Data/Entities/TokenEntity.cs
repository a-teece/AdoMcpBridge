namespace AdoMcpBridge.Core.Data.Entities;

internal sealed class TokenEntity
{
    public string AccessTokenHash { get; set; } = string.Empty;
    public string RefreshTokenHash { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string EntraRefreshTokenEncrypted { get; set; } = string.Empty;
    public string UserObjectId { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public DateTimeOffset RefreshTokenExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
