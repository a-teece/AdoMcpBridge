using System.Collections.Concurrent;

namespace AdoMcpBridge.Core.Tests;

public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, RegisteredClient> _clients = new();
    private readonly ConcurrentDictionary<string, AuthorizationCodeRecord> _codes = new();
    private readonly ConcurrentDictionary<string, TokenRecord> _tokensByAccessHash = new();

    public ValueTask<RegisteredClient?> FindClientAsync(string clientId, CancellationToken ct)
    {
        _clients.TryGetValue(clientId, out var c);
        return ValueTask.FromResult(c);
    }

    public ValueTask AddClientAsync(RegisteredClient client, CancellationToken ct)
    {
        if (!_clients.TryAdd(client.ClientId, client))
        {
            throw new InvalidOperationException($"Client '{client.ClientId}' already exists.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask AddAuthorizationCodeAsync(AuthorizationCodeRecord code, CancellationToken ct)
    {
        if (!_codes.TryAdd(code.Code, code))
        {
            throw new InvalidOperationException($"Authorization code already present.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<AuthorizationCodeRecord?> ConsumeAuthorizationCodeAsync(string code, CancellationToken ct)
    {
        _codes.TryRemove(code, out var rec);
        return ValueTask.FromResult(rec);
    }

    public ValueTask AddTokenAsync(TokenRecord token, CancellationToken ct)
    {
        if (!_tokensByAccessHash.TryAdd(token.AccessTokenHash, token))
        {
            throw new InvalidOperationException("Access token hash collision.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<TokenRecord?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct)
    {
        _tokensByAccessHash.TryGetValue(accessTokenHash, out var t);
        return ValueTask.FromResult(t);
    }

    public ValueTask<TokenRecord?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct)
    {
        TokenRecord? match = null;
        foreach (var t in _tokensByAccessHash.Values)
        {
            if (t.RefreshTokenHash == refreshTokenHash)
            {
                match = t;
                break;
            }
        }
        return ValueTask.FromResult(match);
    }

    public ValueTask RevokeTokenAsync(string refreshTokenHash, CancellationToken ct)
    {
        foreach (var kv in _tokensByAccessHash)
        {
            if (kv.Value.RefreshTokenHash == refreshTokenHash)
            {
                _tokensByAccessHash.TryRemove(kv.Key, out _);
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ReplaceTokenAsync(TokenRecord oldToken, TokenRecord newToken, CancellationToken ct)
    {
        _tokensByAccessHash.TryRemove(oldToken.AccessTokenHash, out _);
        if (!_tokensByAccessHash.TryAdd(newToken.AccessTokenHash, newToken))
        {
            throw new InvalidOperationException("Replacement token hash collision.");
        }
        return ValueTask.CompletedTask;
    }
}
