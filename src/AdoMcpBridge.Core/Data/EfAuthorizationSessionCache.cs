using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Data.Entities;
using AdoMcpBridge.Core.OAuth;
using Microsoft.EntityFrameworkCore;

namespace AdoMcpBridge.Core.Data;

/// <summary>
/// SQL-backed <see cref="IAuthorizationSessionCache"/>. An authorization
/// session spans human time (consent page, Entra sign-in with MFA) while
/// the Container App can scale to zero, hop replicas, or restart — an
/// in-memory cache loses the session in all three cases (issue #33).
/// Expired rows are purged on read; the 10-minute TTL keeps the table
/// tiny.
/// </summary>
public sealed class EfAuthorizationSessionCache : IAuthorizationSessionCache
{
    private readonly BridgeDbContext _db;
    private readonly IClock _clock;

    public EfAuthorizationSessionCache(BridgeDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async ValueTask PutAsync(AuthorizationSession s, CancellationToken ct)
    {
        _db.Sessions.Add(new AuthorizationSessionEntity
        {
            SessionId = s.SessionId,
            ClientId = s.ClientId,
            RedirectUri = s.RedirectUri,
            ClientCodeChallenge = s.ClientCodeChallenge,
            ClientCodeChallengeMethod = s.ClientCodeChallengeMethod,
            ClientState = s.ClientState,
            EntraCodeVerifier = s.EntraCodeVerifier,
            EntraState = s.EntraState,
            ExpiresAt = s.ExpiresAt,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask<AuthorizationSession?> GetAsync(string sessionId, CancellationToken ct)
    {
        var e = await _db.Sessions.SingleOrDefaultAsync(x => x.SessionId == sessionId, ct);
        return await MaterializeAsync(e, ct);
    }

    public async ValueTask<AuthorizationSession?> GetByEntraStateAsync(string entraState, CancellationToken ct)
    {
        var e = await _db.Sessions.SingleOrDefaultAsync(x => x.EntraState == entraState, ct);
        return await MaterializeAsync(e, ct);
    }

    public async ValueTask RemoveAsync(string sessionId, CancellationToken ct)
    {
        await _db.Sessions.Where(x => x.SessionId == sessionId).ExecuteDeleteAsync(ct);
    }

    private async ValueTask<AuthorizationSession?> MaterializeAsync(AuthorizationSessionEntity? e, CancellationToken ct)
    {
        if (e is null) return null;
        if (e.ExpiresAt <= _clock.UtcNow)
        {
            _db.Sessions.Remove(e);
            await _db.SaveChangesAsync(ct);
            return null;
        }

        return new AuthorizationSession(
            e.SessionId, e.ClientId, e.RedirectUri, e.ClientCodeChallenge,
            e.ClientCodeChallengeMethod, e.ClientState, e.EntraCodeVerifier,
            e.EntraState, e.ExpiresAt);
    }
}
