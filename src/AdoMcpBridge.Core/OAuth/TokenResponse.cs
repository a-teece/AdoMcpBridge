using System.Text.Json.Serialization;

namespace AdoMcpBridge.Core.OAuth;

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = "";
    [JsonPropertyName("token_type")] public string TokenType { get; init; } = "Bearer";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    [JsonPropertyName("scope")] public string Scope { get; init; } = "mcp";
}
