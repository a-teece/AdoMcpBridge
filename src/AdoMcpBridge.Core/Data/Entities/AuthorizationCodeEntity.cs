namespace AdoMcpBridge.Core.Data.Entities;

internal sealed class AuthorizationCodeEntity
{
    public string Code { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string PkceChallenge { get; set; } = string.Empty;
    public string PkceMethod { get; set; } = "S256";
    public string EntraRefreshTokenEncrypted { get; set; } = string.Empty;
    public string UserObjectId { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
