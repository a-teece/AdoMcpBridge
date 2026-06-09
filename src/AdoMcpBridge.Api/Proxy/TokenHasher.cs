using System.Security.Cryptography;
using System.Text;

namespace AdoMcpBridge.Api.Proxy;

internal static class TokenHasher
{
    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
