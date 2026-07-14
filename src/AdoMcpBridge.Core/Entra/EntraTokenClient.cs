using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AdoMcpBridge.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.Entra;

/// <summary>
/// Production <see cref="IEntraTokenClient"/> that talks to the Entra v2.0 token
/// endpoint directly using a certificate-signed <c>private_key_jwt</c> client
/// assertion. Talking to the endpoint directly (rather than via MSAL) is required
/// because the bridge must persist the raw Entra <c>refresh_token</c>, which MSAL
/// deliberately hides from callers.
/// </summary>
public sealed class EntraTokenClient : IEntraTokenClient
{
    private const string ClientAssertionType =
        "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly HttpClient _http;
    private readonly ICertificateProvider _certs;
    private readonly EntraOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<EntraTokenClient> _log;

    public EntraTokenClient(
        HttpClient http,
        ICertificateProvider certs,
        IOptions<EntraOptions> options,
        IClock clock,
        ILogger<EntraTokenClient> log)
    {
        _http = http;
        _certs = certs;
        _options = options.Value;
        _clock = clock;
        _log = log;
    }

    public ValueTask<EntraTokenResult> ExchangeAuthorizationCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        };
        return RequestTokenAsync(form, EntraAuthFailure.AuthorizationCodeRejected, _options.Scopes, ct);
    }

    public ValueTask<EntraTokenResult> AcquireAdoTokenAsync(string entraRefreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = entraRefreshToken,
        };
        return RequestTokenAsync(form, EntraAuthFailure.RefreshRejected, _options.Scopes, ct);
    }

    public ValueTask<EntraTokenResult> AcquireAdoRestTokenAsync(string entraRefreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = entraRefreshToken,
        };
        return RequestTokenAsync(form, EntraAuthFailure.RefreshRejected, _options.AdoRestScopes, ct);
    }

    private async ValueTask<EntraTokenResult> RequestTokenAsync(
        Dictionary<string, string> form, EntraAuthFailure rejectionFailure,
        IEnumerable<string> scopes, CancellationToken ct)
    {
        var tokenEndpoint = TokenEndpoint();
        var cert = await _certs.GetCertificateAsync(ct).ConfigureAwait(false);

        form["client_id"] = _options.ClientId;
        form["scope"] = string.Join(' ', scopes);
        form["client_assertion_type"] = ClientAssertionType;
        form["client_assertion"] = BuildClientAssertion(cert, tokenEndpoint);

        HttpResponseMessage response;
        string body;
        try
        {
            response = await _http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new EntraAuthException(EntraAuthFailure.Transport, null, null,
                "Transport error contacting Entra.", ex);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            var errorCode = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            _log.LogWarning("Entra {Grant} grant rejected: status={Status} error={Error}",
                form["grant_type"], (int)response.StatusCode, errorCode);
            throw new EntraAuthException(
                rejectionFailure, (int)response.StatusCode, errorCode, "Entra rejected the token request.");
        }

        var accessToken = root.TryGetProperty("access_token", out var atEl) ? atEl.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new EntraAuthException(EntraAuthFailure.Unknown, null, null, "Entra response missing access_token.");
        }

        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new EntraAuthException(EntraAuthFailure.Unknown, null, null,
                "Entra response did not contain a refresh_token; offline_access scope required.");
        }

        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        var idToken = root.TryGetProperty("id_token", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(idToken))
        {
            throw new EntraAuthException(EntraAuthFailure.Unknown, null, null, "Entra response missing id_token.");
        }

        var (oid, upn) = ParseIdentity(idToken);

        return new EntraTokenResult(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: _clock.UtcNow.AddSeconds(expiresIn),
            UserObjectId: oid,
            UserPrincipalName: upn);
    }

    private string TokenEndpoint()
    {
        var authority = _options.Authority.TrimEnd('/');
        if (authority.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase))
        {
            authority = authority[..^"/v2.0".Length];
        }
        return $"{authority}/oauth2/v2.0/token";
    }

    private string BuildClientAssertion(X509Certificate2 cert, string audience)
    {
        var now = _clock.UtcNow.ToUnixTimeSeconds();
        var header = new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["x5t"] = Base64Url(cert.GetCertHash()),
        };
        var payload = new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["iss"] = _options.ClientId,
            ["sub"] = _options.ClientId,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["nbf"] = now,
            ["exp"] = now + 300,
            ["iat"] = now,
        };

        var signingInput =
            $"{Base64Url(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload))}";

        using var rsa = cert.GetRSAPrivateKey()
            ?? throw new EntraAuthException(EntraAuthFailure.CertificateUnavailable, null, null,
                "Certificate has no RSA private key for client assertion signing.");
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static (string Oid, string Upn) ParseIdentity(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            throw new EntraAuthException(EntraAuthFailure.Unknown, null, null, "Malformed id_token.");
        }

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        var oid = root.TryGetProperty("oid", out var o) ? o.GetString() : null;
        if (string.IsNullOrEmpty(oid))
        {
            throw new EntraAuthException(EntraAuthFailure.Unknown, null, null, "id_token missing oid claim.");
        }
        var upn = root.TryGetProperty("preferred_username", out var u) ? u.GetString() ?? string.Empty : string.Empty;
        return (oid, upn);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (s.Length % 4)) % 4;
        return Convert.FromBase64String(s + new string('=', padding));
    }
}
