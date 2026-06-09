namespace AdoMcpBridge.Core.KeyVault;

public sealed class KeyVaultOptions
{
    public const string SectionName = "AdoMcp:KeyVault";
    public string VaultUri { get; set; } = string.Empty;
    public string DekName { get; set; } = "token-dek";
}
