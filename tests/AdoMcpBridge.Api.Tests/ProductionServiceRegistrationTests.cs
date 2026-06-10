using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Data;
using AdoMcpBridge.Core.Entra;
using AdoMcpBridge.Core.KeyVault;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Api.Tests;

/// <summary>
/// Guards the production DI wiring. The other API test fixtures
/// remove-and-replace these services with fakes, which means a
/// Program.cs that forgets to register the real implementations still
/// passes every functional test — but ships a container that crashes
/// at startup. These assertions inspect the raw service collection so
/// no test double can mask the gap.
/// </summary>
public class ProductionServiceRegistrationTests
{
    [Fact]
    public void Program_registers_production_implementations()
    {
        IServiceCollection? captured = null;
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("AdoMcp:Database:ConnectionString", "Server=localhost;Database=di-test;");
                b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["AdoMcp:Issuer"] = "https://test.local",
                        ["AdoMcp:Entra:TenantId"] = "tid",
                        ["AdoMcp:Entra:ClientId"] = "cid",
                    }));
                b.ConfigureServices(s => captured = s);
            });
        _ = factory.Server; // force the host to build

        Assert.NotNull(captured);
        Assert.Contains(captured, d =>
            d.ServiceType == typeof(ITokenStore) && d.ImplementationType == typeof(EfTokenStore));
        Assert.Contains(captured, d =>
            d.ServiceType == typeof(IKeyVaultEncryptor) && d.ImplementationType == typeof(KeyVaultEncryptor));
        Assert.Contains(captured, d => d.ServiceType == typeof(IEntraTokenClient));
        Assert.Contains(captured, d =>
            d.ServiceType == typeof(ICertificateProvider) && d.ImplementationType == typeof(CertificateProvider));
    }
}
