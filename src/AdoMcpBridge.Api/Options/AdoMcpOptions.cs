namespace AdoMcpBridge.Api.Options;

public sealed class AdoMcpOptions
{
    public string Issuer { get; set; } = "https://localhost:5001";
    public string UpstreamBaseUrl { get; set; } = "https://mcp.dev.azure.com";

    /// <summary>
    /// Organization applied to custom <c>ado_bridge_*</c> tool calls when the caller
    /// omits <c>organization</c>. Empty (the default) preserves the required-argument
    /// behaviour; single-tenant deployments set it via <c>AdoMcp__DefaultOrganization</c>.
    /// A caller-supplied <c>organization</c> is never overridden.
    /// </summary>
    public string DefaultOrganization { get; set; } = "";

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
