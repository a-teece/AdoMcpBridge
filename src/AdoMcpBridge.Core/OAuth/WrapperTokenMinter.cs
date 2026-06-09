using System.Security.Cryptography;
using System.Text;
using AdoMcpBridge.Core.Abstractions;

namespace AdoMcpBridge.Core.OAuth;

public sealed class WrapperTokenMinter
{
    private readonly IClock _clock;
    public WrapperTokenMinter(IClock clock) => _clock = clock;

    public DateTimeOffset AccessTokenExpiresAt => _clock.UtcNow.AddHours(1);
    public DateTimeOffset RefreshTokenExpiresAt => _clock.UtcNow.AddDays(14);
    public DateTimeOffset AuthorizationCodeExpiresAt => _clock.UtcNow.AddSeconds(60);

    public MintedTokenPair MintPair() => new(MintOpaque(), MintOpaque());

    public string MintOpaque()
    {
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToBase64String(buf).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string Hash(string token)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.ASCII.GetBytes(token), hash);
        return Convert.ToHexStringLower(hash);
    }
}
