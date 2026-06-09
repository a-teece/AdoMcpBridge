using Microsoft.Identity.Client;

namespace AdoMcpBridge.Core.Entra;

public interface IMsalClientFactory
{
    ValueTask<IConfidentialClientApplication> CreateAsync(CancellationToken ct);
}
