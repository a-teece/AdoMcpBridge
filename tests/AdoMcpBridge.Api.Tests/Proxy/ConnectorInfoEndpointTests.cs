using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.Proxy;

public sealed class ConnectorInfoEndpointTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _factory;
    public ConnectorInfoEndpointTests(BridgeApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns_connector_card_json()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/connector-info.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        doc.GetProperty("name").GetString().Should().Be("Azure DevOps (via Bridge)");
        doc.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        doc.GetProperty("auth_metadata_url").GetString()
            .Should().EndWith("/.well-known/oauth-authorization-server");
    }
}
