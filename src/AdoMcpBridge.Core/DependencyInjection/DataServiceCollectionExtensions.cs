using System.Diagnostics.CodeAnalysis;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Data;
using AdoMcpBridge.Core.KeyVault;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.DependencyInjection;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddBridgeDataServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration["AdoMcp:Database:ConnectionString"]
            ?? throw new InvalidOperationException("AdoMcp:Database:ConnectionString is required.");

        services.AddDbContext<BridgeDbContext>(o => o.UseSqlServer(connectionString));

        services.AddScoped<ITokenStore, EfTokenStore>();

        services.Configure<KeyVaultOptions>(configuration.GetSection(KeyVaultOptions.SectionName));

        services.AddSingleton(BuildCryptographyClient);
        services.AddSingleton<IKeyVaultEncryptor, KeyVaultEncryptor>();

        return services;
    }

    [ExcludeFromCodeCoverage(Justification = "Constructs a live Azure CryptographyClient; covered by integration tests against a real Key Vault, not unit-testable.")]
    private static CryptographyClient BuildCryptographyClient(IServiceProvider sp)
    {
        var opts = sp.GetRequiredService<IOptions<KeyVaultOptions>>().Value;
        var keyClient = new KeyClient(new Uri(opts.VaultUri), new DefaultAzureCredential());
        var key = keyClient.GetKey(opts.DekName).Value;
        return new CryptographyClient(key.Id, new DefaultAzureCredential());
    }
}
