using AdoMcpBridge.Core.BlobStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Core.Tests.BlobStorage;

public sealed class BlobStorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBlobSlotStore_registers_IBlobSlotStore()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdoMcp:BlobStorage:AccountUri"] = "https://test.blob.core.windows.net/",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBlobSlotStore(cfg);

        // Verify the service descriptor is present without resolving (which
        // would invoke BuildBlobServiceClient and DefaultAzureCredential).
        Assert.Contains(services, d => d.ServiceType == typeof(IBlobSlotStore));
    }
}
