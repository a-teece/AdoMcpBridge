using System.Text.Json.Serialization;

namespace AdoMcpBridge.Api.Endpoints;

public sealed class RegistrationRequest
{
    [JsonPropertyName("client_name")] public string? ClientName { get; set; }
    [JsonPropertyName("redirect_uris")] public List<string>? RedirectUris { get; set; }
}

public sealed class RegistrationResponse
{
    [JsonPropertyName("client_id")] public string ClientId { get; set; } = "";
    [JsonPropertyName("client_name")] public string ClientName { get; set; } = "";
    [JsonPropertyName("redirect_uris")] public List<string> RedirectUris { get; set; } = new();
    [JsonPropertyName("token_endpoint_auth_method")] public string TokenEndpointAuthMethod => "none";
    [JsonPropertyName("grant_types")] public string[] GrantTypes => new[] { "authorization_code", "refresh_token" };
    [JsonPropertyName("response_types")] public string[] ResponseTypes => new[] { "code" };
}
