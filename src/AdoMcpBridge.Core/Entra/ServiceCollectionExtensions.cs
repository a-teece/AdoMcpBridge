using AdoMcpBridge.Core.Abstractions;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.Entra;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntraClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EntraOptions>()
            .Bind(configuration.GetSection(EntraOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.TenantId), "Entra TenantId required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Entra ClientId required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.CertificateName), "Entra CertificateName required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.KeyVaultUri), "Entra KeyVaultUri required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Authority), "Entra Authority required")
            .Validate(o => o.Scopes.Count > 0, "At least one scope required");

        services.AddSingleton<CertificateClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<EntraOptions>>().Value;
            return new CertificateClient(new Uri(opts.KeyVaultUri), new DefaultAzureCredential());
        });
        services.AddSingleton<SecretClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<EntraOptions>>().Value;
            return new SecretClient(new Uri(opts.KeyVaultUri), new DefaultAzureCredential());
        });

        services.AddSingleton<ICertificateProvider, CertificateProvider>();
        services.AddHttpClient<IEntraTokenClient, EntraTokenClient>();

        return services;
    }
}
