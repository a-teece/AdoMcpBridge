using System.Text.Json;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdoMcpBridge.Core.Data;

public sealed class EfTokenStore : ITokenStore
{
    private readonly BridgeDbContext _db;
    public EfTokenStore(BridgeDbContext db) => _db = db;

    public async ValueTask<RegisteredClient?> FindClientAsync(string clientId, CancellationToken ct)
    {
        var e = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
        return e is null ? null : ToRecord(e);
    }

    public async ValueTask AddClientAsync(RegisteredClient client, CancellationToken ct)
    {
        _db.Clients.Add(new ClientEntity
        {
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            RedirectUrisJson = JsonSerializer.Serialize(client.RedirectUris),
            CreatedAt = client.CreatedAt,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask AddAuthorizationCodeAsync(AuthorizationCodeRecord code, CancellationToken ct)
    {
        _db.AuthorizationCodes.Add(new AuthorizationCodeEntity
        {
            Code = code.Code,
            ClientId = code.ClientId,
            RedirectUri = code.RedirectUri,
            PkceChallenge = code.PkceChallenge,
            PkceMethod = code.PkceMethod,
            EntraRefreshTokenEncrypted = code.EntraRefreshTokenEncrypted,
            UserObjectId = code.UserObjectId,
            UserPrincipalName = code.UserPrincipalName,
            ExpiresAt = code.ExpiresAt,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask<AuthorizationCodeRecord?> ConsumeAuthorizationCodeAsync(string code, CancellationToken ct)
    {
        var e = await _db.AuthorizationCodes.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (e is null) return null;
        _db.AuthorizationCodes.Remove(e);
        await _db.SaveChangesAsync(ct);
        return new AuthorizationCodeRecord(
            e.Code, e.ClientId, e.RedirectUri, e.PkceChallenge, e.PkceMethod,
            e.EntraRefreshTokenEncrypted, e.UserObjectId, e.UserPrincipalName, e.ExpiresAt);
    }

    public async ValueTask AddTokenAsync(TokenRecord token, CancellationToken ct)
    {
        _db.Tokens.Add(ToEntity(token));
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask<TokenRecord?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct)
    {
        var e = await _db.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.AccessTokenHash == accessTokenHash, ct);
        return e is null ? null : ToRecord(e);
    }

    public async ValueTask<TokenRecord?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct)
    {
        var e = await _db.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.RefreshTokenHash == refreshTokenHash, ct);
        return e is null ? null : ToRecord(e);
    }

    public async ValueTask RevokeTokenAsync(string refreshTokenHash, CancellationToken ct)
    {
        var e = await _db.Tokens.FirstOrDefaultAsync(t => t.RefreshTokenHash == refreshTokenHash, ct);
        if (e is null) return;
        _db.Tokens.Remove(e);
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask ReplaceTokenAsync(TokenRecord oldToken, TokenRecord newToken, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var existing = await _db.Tokens.FirstOrDefaultAsync(t => t.AccessTokenHash == oldToken.AccessTokenHash, ct);
        if (existing is not null) _db.Tokens.Remove(existing);
        _db.Tokens.Add(ToEntity(newToken));
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static RegisteredClient ToRecord(ClientEntity e) => new(
        e.ClientId, e.ClientName,
        JsonSerializer.Deserialize<List<string>>(e.RedirectUrisJson) ?? new List<string>(),
        e.CreatedAt);

    private static TokenRecord ToRecord(TokenEntity e) => new(
        e.AccessTokenHash, e.RefreshTokenHash, e.ClientId, e.EntraRefreshTokenEncrypted,
        e.UserObjectId, e.UserPrincipalName, e.AccessTokenExpiresAt, e.RefreshTokenExpiresAt, e.CreatedAt);

    private static TokenEntity ToEntity(TokenRecord t) => new()
    {
        AccessTokenHash = t.AccessTokenHash,
        RefreshTokenHash = t.RefreshTokenHash,
        ClientId = t.ClientId,
        EntraRefreshTokenEncrypted = t.EntraRefreshTokenEncrypted,
        UserObjectId = t.UserObjectId,
        UserPrincipalName = t.UserPrincipalName,
        AccessTokenExpiresAt = t.AccessTokenExpiresAt,
        RefreshTokenExpiresAt = t.RefreshTokenExpiresAt,
        CreatedAt = t.CreatedAt,
    };
}
