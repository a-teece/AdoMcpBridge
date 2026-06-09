namespace AdoMcpBridge.Core.Data.Entities;

internal sealed class ClientEntity
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string RedirectUrisJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}
