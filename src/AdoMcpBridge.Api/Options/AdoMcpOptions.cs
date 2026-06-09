namespace AdoMcpBridge.Api.Options;

public sealed class AdoMcpOptions
{
    public string Issuer { get; set; } = "https://localhost:5001";
    public string UpstreamBaseUrl { get; set; } = "https://mcp.dev.azure.com";
    public EntraOptions Entra { get; set; } = new();
}

public sealed class EntraOptions
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Authority { get; set; } = "";
    public string[] Scopes { get; set; } =
        new[] { "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation", "offline_access" };
}
