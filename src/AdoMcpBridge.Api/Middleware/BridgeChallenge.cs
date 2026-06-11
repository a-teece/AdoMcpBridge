namespace AdoMcpBridge.Api.Middleware;

/// <summary>
/// Builds the bridge's RFC 6750 / RFC 9728 WWW-Authenticate challenge.
/// Used by both the bearer middleware and the proxy response transform
/// so every 401 leaving the bridge points clients at the bridge's own
/// protected-resource metadata — never an upstream's.
/// </summary>
internal static class BridgeChallenge
{
    public static string For(string issuer, PathString requestPath, string? errorCode = null)
    {
        var metadataUrl = $"{issuer.TrimEnd('/')}/.well-known/oauth-protected-resource{requestPath.Value}";
        var error = errorCode is null ? string.Empty : $", error=\"{errorCode}\"";
        return $"Bearer realm=\"ado-mcp-bridge\"{error}, resource_metadata=\"{metadataUrl}\"";
    }
}
