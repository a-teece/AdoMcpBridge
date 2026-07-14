namespace AdoMcpBridge.Core.Entra;

public sealed class EntraOptions
{
    public const string SectionName = "AdoMcp:Entra";

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string CertificateName { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string KeyVaultUri { get; set; } = string.Empty;
    public IList<string> Scopes { get; set; } = new List<string>();

    /// <summary>
    /// Delegated scopes requested for the classic Azure DevOps REST API
    /// (resource <c>499b84ac-1321-427f-aa17-267ca6975798</c>) that the native
    /// bridge tools call directly. Deliberately separate from <see cref="Scopes"/>:
    /// that list targets the Remote MCP server resource, which rejects
    /// classic-ADO-audience tokens and vice versa. Like <see cref="Scopes"/>, this
    /// list must include <c>openid</c>, <c>profile</c> and <c>offline_access</c>
    /// so the token response carries the id_token and refresh_token the shared
    /// parsing path requires.
    /// </summary>
    public IList<string> AdoRestScopes { get; set; } = new List<string>();

    /// <summary>
    /// Password used to load the PFX returned by Key Vault. Default empty; rotate via KV policy.
    /// </summary>
    public string PfxPassword { get; set; } = string.Empty;
}
