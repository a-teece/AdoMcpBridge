using System.Text.Json.Serialization;

namespace AdoMcpBridge.Core.OAuth;

public sealed record AuthorizationServerMetadata(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("registration_endpoint")] string RegistrationEndpoint,
    [property: JsonPropertyName("revocation_endpoint")] string RevocationEndpoint,
    [property: JsonPropertyName("response_types_supported")] IReadOnlyList<string> ResponseTypesSupported,
    [property: JsonPropertyName("grant_types_supported")] IReadOnlyList<string> GrantTypesSupported,
    [property: JsonPropertyName("code_challenge_methods_supported")] IReadOnlyList<string> CodeChallengeMethodsSupported,
    [property: JsonPropertyName("token_endpoint_auth_methods_supported")] IReadOnlyList<string> TokenEndpointAuthMethodsSupported,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported)
{
    private static readonly string[] ResponseTypes = { "code" };
    private static readonly string[] GrantTypes = { "authorization_code", "refresh_token" };
    private static readonly string[] CodeChallengeMethods = { "S256" };
    private static readonly string[] AuthMethods = { "none" };
    private static readonly string[] Scopes = { "mcp" };

    public static AuthorizationServerMetadata ForIssuer(string issuer)
    {
        var trimmed = issuer.TrimEnd('/');
        return new AuthorizationServerMetadata(
            trimmed,
            $"{trimmed}/authorize",
            $"{trimmed}/token",
            $"{trimmed}/register",
            $"{trimmed}/revoke",
            ResponseTypes,
            GrantTypes,
            CodeChallengeMethods,
            AuthMethods,
            Scopes);
    }
}
