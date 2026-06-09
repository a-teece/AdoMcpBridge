namespace AdoMcpBridge.Core.OAuth;

public sealed record AuthorizeRequest(
    string ResponseType,
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string CodeChallengeMethod,
    string State,
    string? Scope);
