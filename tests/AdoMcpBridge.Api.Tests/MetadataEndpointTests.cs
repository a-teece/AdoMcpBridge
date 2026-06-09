using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests;

public sealed class MetadataEndpointTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _factory;
    public MetadataEndpointTests(BridgeApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Metadata_endpoint_returns_expected_document()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/.well-known/oauth-authorization-server");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var doc = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        doc!["issuer"].ToString().Should().Be("https://test.local");
        doc["authorization_endpoint"].ToString().Should().Be("https://test.local/authorize");
    }
}
