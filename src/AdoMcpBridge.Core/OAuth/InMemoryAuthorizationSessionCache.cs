using System.Collections.Concurrent;
using AdoMcpBridge.Core.Abstractions;

namespace AdoMcpBridge.Core.OAuth;

public sealed class InMemoryAuthorizationSessionCache : IAuthorizationSessionCache
{
    private readonly ConcurrentDictionary<string, AuthorizationSession> _sessions = new();
    private readonly IClock _clock;
    public InMemoryAuthorizationSessionCache(IClock clock) => _clock = clock;

    public ValueTask PutAsync(AuthorizationSession s, CancellationToken ct)
    {
        _sessions[s.SessionId] = s;
        return ValueTask.CompletedTask;
    }

    public ValueTask<AuthorizationSession?> GetAsync(string sessionId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var s)) return new(default(AuthorizationSession?));
        if (s.ExpiresAt <= _clock.UtcNow)
        {
            _sessions.TryRemove(sessionId, out _);
            return new(default(AuthorizationSession?));
        }
        return new(s);
    }

    public ValueTask RemoveAsync(string sessionId, CancellationToken ct)
    {
        _sessions.TryRemove(sessionId, out _);
        return ValueTask.CompletedTask;
    }
}
