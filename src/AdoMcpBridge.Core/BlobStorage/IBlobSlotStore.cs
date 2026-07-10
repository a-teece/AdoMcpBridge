namespace AdoMcpBridge.Core.BlobStorage;

public sealed record UploadSlot(string SlotId, Uri UploadUrl, DateTimeOffset ExpiresAt);

public interface IBlobSlotStore
{
    /// <summary>Creates a write-only SAS-URL slot for a client upload.</summary>
    Task<UploadSlot> CreateSlotAsync(CancellationToken ct = default);

    /// <summary>Reads the raw bytes from a previously uploaded slot.</summary>
    Task<byte[]> ReadSlotAsync(string slotId, CancellationToken ct = default);

    /// <summary>Deletes the slot blob (called on successful field write).</summary>
    Task DeleteSlotAsync(string slotId, CancellationToken ct = default);
}
