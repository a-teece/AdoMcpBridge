using System.Diagnostics.CodeAnalysis;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.BlobStorage;

public static class BlobStorageServiceCollectionExtensions
{
    public static IServiceCollection AddBlobSlotStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BlobStorageOptions>()
            .Bind(configuration.GetSection(BlobStorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountUri),
                "AdoMcp:BlobStorage:AccountUri is required.")
            .ValidateOnStart();

        services.AddSingleton(BuildBlobServiceClient);
        services.AddSingleton<IBlobSlotStore, BlobSlotStore>();

        return services;
    }

    [ExcludeFromCodeCoverage(Justification =
        "Constructs a live Azure BlobServiceClient; covered by integration smoke tests, not unit-testable.")]
    private static BlobServiceClient BuildBlobServiceClient(IServiceProvider sp)
    {
        var opts = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
        return new BlobServiceClient(new Uri(opts.AccountUri), new DefaultAzureCredential());
    }
}
