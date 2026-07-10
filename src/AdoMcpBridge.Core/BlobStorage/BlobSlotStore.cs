using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.BlobStorage;

[ExcludeFromCodeCoverage(Justification =
    "All methods make live Azure Blob SDK calls requiring a real storage account; covered by post-deploy integration smoke tests, not unit-testable.")]
internal sealed class BlobSlotStore : IBlobSlotStore
{
    private readonly BlobServiceClient _service;
    private readonly BlobStorageOptions _options;
    private readonly ILogger<BlobSlotStore> _logger;

    public BlobSlotStore(
        BlobServiceClient service,
        IOptions<BlobStorageOptions> options,
        ILogger<BlobSlotStore> logger)
    {
        _service = service;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadSlot> CreateSlotAsync(CancellationToken ct = default)
    {
        var slotId = Guid.NewGuid().ToString("N");
        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-1); // 1-min skew buffer
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(_options.SlotTtlMinutes);

        // User-delegation key: signed by the MI identity — no account key is
        // ever materialised in the application.  Requires the Storage Blob
        // Delegator role on the storage account.
        var delegationKey = await _service
            .GetUserDelegationKeyAsync(startsOn, expiresOn, ct)
            .ConfigureAwait(false);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _options.ContainerName,
            BlobName = slotId,
            Resource = "b",
            StartsOn = startsOn,
            ExpiresOn = expiresOn,
        };
        // Write + Create lets the client PUT a new blob without being able
        // to read, list, or delete.
        sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

        var sasParams = sasBuilder.ToSasQueryParameters(delegationKey, _service.AccountName);
        var blobUri = _service
            .GetBlobContainerClient(_options.ContainerName)
            .GetBlobClient(slotId)
            .Uri;

        var uploadUrl = new UriBuilder(blobUri) { Query = sasParams.ToString() }.Uri;

        _logger.LogInformation("Created upload slot {SlotId} expiring {ExpiresOn}", slotId, expiresOn);
        return new UploadSlot(slotId, uploadUrl, expiresOn);
    }

    public async Task<byte[]> ReadSlotAsync(string slotId, CancellationToken ct = default)
    {
        var blob = _service.GetBlobContainerClient(_options.ContainerName).GetBlobClient(slotId);
        var response = await blob.DownloadContentAsync(ct).ConfigureAwait(false);
        return response.Value.Content.ToArray();
    }

    public async Task DeleteSlotAsync(string slotId, CancellationToken ct = default)
    {
        var blob = _service.GetBlobContainerClient(_options.ContainerName).GetBlobClient(slotId);
        await blob.DeleteIfExistsAsync(cancellationToken: ct).ConfigureAwait(false);
        _logger.LogInformation("Deleted upload slot {SlotId}", slotId);
    }
}
