namespace AdoMcpBridge.Core.BlobStorage;

public sealed record UploadSlot(string SlotId, Uri UploadUrl, DateTimeOffset ExpiresAt);

public sealed record DownloadSlot(string SlotId, Uri DownloadUrl, DateTimeOffset ExpiresAt);

public interface IBlobSlotStore
{
    /// <summary>Creates a write-only SAS-URL slot for a client upload.</summary>
    Task<UploadSlot> CreateSlotAsync(CancellationToken ct = default);

    /// <summary>Reads the raw bytes from a previously uploaded slot.</summary>
    Task<byte[]> ReadSlotAsync(string slotId, CancellationToken ct = default);

    /// <summary>Deletes the slot blob (called on successful field write).</summary>
    Task DeleteSlotAsync(string slotId, CancellationToken ct = default);

    /// <summary>
    /// Writes <paramref name="content"/> to a new blob (server-side, via the MI) and returns a
    /// read-only SAS URL the client can pull directly — so large binary content (e.g. a
    /// work-item attachment) never routes through the model. The optional
    /// <paramref name="contentType"/> is stored on the blob so the download carries the right
    /// MIME type. The blob is swept by the same lifecycle policy that expires abandoned upload
    /// slots; the SAS itself is short-lived.
    /// </summary>
    Task<DownloadSlot> CreateDownloadSlotAsync(
        byte[] content, string? contentType = null, CancellationToken ct = default);
}
