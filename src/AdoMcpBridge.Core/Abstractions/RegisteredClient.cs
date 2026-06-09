namespace AdoMcpBridge.Core.Abstractions;

public sealed record RegisteredClient(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    DateTimeOffset CreatedAt);
