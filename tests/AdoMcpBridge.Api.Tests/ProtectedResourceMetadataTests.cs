using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests;

/// <summary>
/// RFC 9728: MCP clients resolve the bridge's protected-resource
/// metadata to discover the authorization server and validate that the
/// resource matches the URL they connected to.
/// </summary>
public sealed class ProtectedResourceMetadataTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _f;
    public ProtectedResourceMetadataTests(BridgeApiFactory f) => _f = f;

    [Fact]
    public async Task Metadata_for_org_path_names_bridge_resource_and_as()
    {
        var client = _f.CreateClient();
        var resp = await client.GetAsync("/.well-known/oauth-protected-resource/mcp/Enate");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["resource"].ToString().Should().Be("https://test.local/mcp/Enate");
        body["authorization_servers"].ToString().Should().Contain("https://test.local");
        body["bearer_methods_supported"].ToString().Should().Contain("header");
    }

    [Fact]
    public async Task Metadata_for_bare_mcp_path_uses_mcp_resource()
    {
        var client = _f.CreateClient();
        var resp = await client.GetAsync("/.well-known/oauth-protected-resource/mcp");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        body!["resource"].ToString().Should().Be("https://test.local/mcp");
    }
}
