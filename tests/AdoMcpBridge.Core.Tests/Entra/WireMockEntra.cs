using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AdoMcpBridge.Core.Tests.Entra;

internal sealed class WireMockEntra : IAsyncDisposable
{
    public WireMockServer Server { get; }
    public string TenantId { get; } = "11111111-1111-1111-1111-111111111111";
    public string Authority => $"{Server.Url}/{TenantId}/v2.0";

    private readonly RSA _signingKey = RSA.Create(2048);

    private WireMockEntra()
    {
        Server = WireMockServer.Start();
        SetupDiscovery();
        SetupJwks();
    }

    public static WireMockEntra Start() => new();

    private void SetupDiscovery()
    {
        var doc = new
        {
            issuer = $"{Server.Url}/{TenantId}/v2.0",
            authorization_endpoint = $"{Server.Url}/{TenantId}/oauth2/v2.0/authorize",
            token_endpoint = $"{Server.Url}/{TenantId}/oauth2/v2.0/token",
            jwks_uri = $"{Server.Url}/{TenantId}/discovery/v2.0/keys",
            response_modes_supported = new[] { "query", "form_post" },
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token", "client_credentials" },
            token_endpoint_auth_methods_supported = new[] { "private_key_jwt", "client_secret_post" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            subject_types_supported = new[] { "pairwise" },
        };
        Server.Given(Request.Create()
                .WithPath($"/{TenantId}/v2.0/.well-known/openid-configuration").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(doc));
    }

    private void SetupJwks()
    {
        var parameters = _signingKey.ExportParameters(false);
        var jwks = new
        {
            keys = new object[]
            {
                new {
                    kty = "RSA", use = "sig", kid = "kid1",
                    n = Base64UrlEncoder.Encode(parameters.Modulus!),
                    e = Base64UrlEncoder.Encode(parameters.Exponent!),
                }
            }
        };
        Server.Given(Request.Create().WithPath($"/{TenantId}/discovery/v2.0/keys").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(jwks));
    }

    public string IssueIdToken(string oid, string upn)
    {
        var handler = new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var creds = new SigningCredentials(new RsaSecurityKey(_signingKey) { KeyId = "kid1" }, SecurityAlgorithms.RsaSha256);
        var token = handler.CreateJwtSecurityToken(
            issuer: $"{Server.Url}/{TenantId}/v2.0",
            audience: "unused-audience",
            subject: new ClaimsIdentity(new[]
            {
                new Claim("oid", oid),
                new Claim("preferred_username", upn),
                new Claim("tid", TenantId),
            }),
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            issuedAt: DateTime.UtcNow,
            signingCredentials: creds);
        return handler.WriteToken(token);
    }

    public void StubTokenEndpoint(int status, object body)
    {
        Server.Given(Request.Create().WithPath($"/{TenantId}/oauth2/v2.0/token").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(status).WithBodyAsJson(body));
    }

    /// <summary>
    /// Returns the value of a single form field from the most recent request
    /// the server received (the token endpoint sends
    /// <c>application/x-www-form-urlencoded</c> bodies), or <see langword="null"/>
    /// if the field is absent.
    /// </summary>
    public string? LastFormValue(string key)
    {
        var body = Server.LogEntries.Last().RequestMessage.Body ?? string.Empty;
        foreach (var pair in body.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && Uri.UnescapeDataString(kv[0]) == key)
            {
                // FormUrlEncodedContent encodes spaces as '+'; decode those
                // before percent-decoding the rest of the value.
                return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            }
        }

        return null;
    }

    public ValueTask DisposeAsync()
    {
        Server.Stop();
        Server.Dispose();
        _signingKey.Dispose();
        return ValueTask.CompletedTask;
    }
}
