using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace AdoMcpBridge.Core.Entra;

[ExcludeFromCodeCoverage(Justification = "Thin wrapper over MSAL.NET's ConfidentialClientApplicationBuilder fluent chain; exercised end-to-end via MsalEntraTokenClient WireMock integration tests, and the fluent chain itself has no branchable logic.")]
public sealed class MsalClientFactory : IMsalClientFactory
{
    private readonly ICertificateProvider _certs;
    private readonly EntraOptions _options;

    public MsalClientFactory(ICertificateProvider certs, IOptions<EntraOptions> options)
    {
        _certs = certs;
        _options = options.Value;
    }

    public async ValueTask<IConfidentialClientApplication> CreateAsync(CancellationToken ct)
    {
        var cert = await _certs.GetCertificateAsync(ct).ConfigureAwait(false);
        return ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithCertificate(cert, sendX5C: true)
            .WithAuthority(_options.Authority, validateAuthority: false)
            .WithExperimentalFeatures()
            .Build();
    }
}
