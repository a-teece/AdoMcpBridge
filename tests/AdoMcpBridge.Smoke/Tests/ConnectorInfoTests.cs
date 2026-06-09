using System.Net;
using System.Net.Http.Json;
using AdoMcpBridge.Smoke.Models;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("Category", "smoke")]
public class ConnectorInfoTests
{
    [SkippableFact]
    public async Task ConnectorInfo_ReturnsExpectedShape()
    {
        Skip.IfNot(SmokeEnvironment.HasBridgeUrl, $"{SmokeEnvironment.BridgeUrlVar} not set");
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        using var client = SmokeHttpClient.Create(baseUri);

        using var response = await client.GetAsync("/connector-info.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var info = await response.Content.ReadFromJsonAsync<ConnectorInfo>();
        info.Should().NotBeNull();
        info!.Name.Should().NotBeNullOrWhiteSpace();
        info.Description.Should().NotBeNullOrWhiteSpace();
        info.AuthMetadataUrl.Should().EndWith("/.well-known/oauth-authorization-server");
    }
}
