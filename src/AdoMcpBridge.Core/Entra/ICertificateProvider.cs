using System.Security.Cryptography.X509Certificates;

namespace AdoMcpBridge.Core.Entra;

public interface ICertificateProvider
{
    ValueTask<X509Certificate2> GetCertificateAsync(CancellationToken ct);
}
