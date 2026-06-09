using System.Net;
using System.Net.Http.Json;
using AdoMcpBridge.Smoke.Models;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("Category", "smoke")]
public class DiscoveryTests
{
    [SkippableFact]
    public async Task WellKnown_ReturnsExpectedShape()
    {
        Skip.IfNot(SmokeEnvironment.HasBridgeUrl, $"{SmokeEnvironment.BridgeUrlVar} not set");
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        using var client = SmokeHttpClient.Create(baseUri);

        using var response = await client.GetAsync("/.well-known/oauth-authorization-server");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<OAuthMetadata>();
        doc.Should().NotBeNull();
        doc!.Issuer.TrimEnd('/').Should().Be(baseUri.ToString().TrimEnd('/'));
        doc.AuthorizationEndpoint.Should().StartWith(doc.Issuer);
        doc.TokenEndpoint.Should().EndWith("/token");
        doc.RegistrationEndpoint.Should().EndWith("/register");
        doc.RevocationEndpoint.Should().EndWith("/revoke");
        doc.ResponseTypesSupported.Should().Contain("code");
        doc.GrantTypesSupported.Should().Contain("authorization_code");
        doc.GrantTypesSupported.Should().Contain("refresh_token");
        doc.CodeChallengeMethodsSupported.Should().Contain("S256");
    }
}
