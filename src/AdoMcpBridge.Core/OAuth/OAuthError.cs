using System.Text.Json;

namespace AdoMcpBridge.Core.OAuth;

public sealed record OAuthError(string Code, string Description)
{
    public static OAuthError InvalidRequest(string d) => new("invalid_request", d);
    public static OAuthError InvalidClient(string d) => new("invalid_client", d);
    public static OAuthError InvalidGrant(string d) => new("invalid_grant", d);
    public static OAuthError UnsupportedGrantType(string d) => new("unsupported_grant_type", d);
    public static OAuthError UnauthorizedClient(string d) => new("unauthorized_client", d);
    public static OAuthError ServerError(string d) => new("server_error", d);

    public string ToJson() => JsonSerializer.Serialize(
        new { error = Code, error_description = Description });
}
