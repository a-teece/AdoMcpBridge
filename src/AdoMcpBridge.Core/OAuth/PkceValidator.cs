using System.Security.Cryptography;
using System.Text;

namespace AdoMcpBridge.Core.OAuth;

public sealed class PkceValidator
{
    public bool Verify(string verifier, string challenge, string method)
    {
        if (!string.Equals(method, "S256", StringComparison.Ordinal)) return false;
        if (verifier.Length < 43 || verifier.Length > 128) return false;

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.ASCII.GetBytes(verifier), hash);
        var computed = Base64UrlEncode(hash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(challenge));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
