using AdoMcpBridge.Core.Data;
using AdoMcpBridge.Core.DependencyInjection;
using AdoMcpBridge.Core.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Core.Tests.DependencyInjection;

public sealed class DataServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBridgeDataServices_registers_DbContext_TokenStore_and_Options()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdoMcp:Database:ConnectionString"] = "Server=(local);Database=Test;Trusted_Connection=true;",
                ["AdoMcp:KeyVault:VaultUri"] = "https://example.vault.azure.net/",
                ["AdoMcp:KeyVault:DekName"] = "token-dek",
            })
            .Build();

        var services = new ServiceCollection();
        // The host registers IClock; the session cache consumes it.
        services.AddSingleton<AdoMcpBridge.Core.Abstractions.IClock, AdoMcpBridge.Core.Time.SystemClock>();
        services.AddBridgeDataServices(cfg);
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<BridgeDbContext>());
        Assert.IsType<EfTokenStore>(sp.GetRequiredService<ITokenStore>());
        Assert.IsType<EfAuthorizationSessionCache>(
            sp.GetRequiredService<AdoMcpBridge.Core.OAuth.IAuthorizationSessionCache>());
        var kvOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KeyVaultOptions>>().Value;
        Assert.Equal("token-dek", kvOpts.DekName);
    }

    [Fact]
    public void AddBridgeDataServices_without_connection_string_throws()
    {
        var cfg = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddBridgeDataServices(cfg));
        Assert.Contains("ConnectionString", ex.Message, StringComparison.Ordinal);
    }
}
