namespace AdoMcpBridge.Api.Options;

public sealed class AdoMcpOptions
{
    public string Issuer { get; set; } = "https://localhost:5001";
    public string UpstreamBaseUrl { get; set; } = "https://mcp.dev.azure.com";
}
