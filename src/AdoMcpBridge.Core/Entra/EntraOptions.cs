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
    /// Password used to load the PFX returned by Key Vault. Default empty; rotate via KV policy.
    /// </summary>
    public string PfxPassword { get; set; } = string.Empty;
}
