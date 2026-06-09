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
        services.AddBridgeDataServices(cfg);
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<BridgeDbContext>());
        Assert.IsType<EfTokenStore>(sp.GetRequiredService<ITokenStore>());
        var kvOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KeyVaultOptions>>().Value;
        Assert.Equal("token-dek", kvOpts.DekName);
    }
}
