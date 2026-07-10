namespace AdoMcpBridge.Core.BlobStorage;

public sealed class BlobStorageOptions
{
    public const string SectionName = "AdoMcp:BlobStorage";

    public string AccountUri { get; init; } = string.Empty;
    public string ContainerName { get; init; } = "upload-slots";
    public int SlotTtlMinutes { get; init; } = 15;
}
