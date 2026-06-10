namespace AdoMcpBridge.Core.OAuth;

public interface IAuthorizationSessionCache
{
    ValueTask PutAsync(AuthorizationSession s, CancellationToken ct);
    ValueTask<AuthorizationSession?> GetAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Looks up a session by the state value the bridge sent to Entra.
    /// The Entra callback carries only code + state, so state is the
    /// only correlation key available on that leg.
    /// </summary>
    ValueTask<AuthorizationSession?> GetByEntraStateAsync(string entraState, CancellationToken ct);

    ValueTask RemoveAsync(string sessionId, CancellationToken ct);
}
