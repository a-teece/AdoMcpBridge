namespace AdoMcpBridge.Core.OAuth;

public interface IAuthorizationSessionCache
{
    ValueTask PutAsync(AuthorizationSession s, CancellationToken ct);
    ValueTask<AuthorizationSession?> GetAsync(string sessionId, CancellationToken ct);
    ValueTask RemoveAsync(string sessionId, CancellationToken ct);
}
