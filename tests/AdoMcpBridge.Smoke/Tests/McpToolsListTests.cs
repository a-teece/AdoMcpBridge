using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AdoMcpBridge.Smoke.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("Category", "smoke")]
public class McpToolsListTests
{
    private readonly ITestOutputHelper _output;
    public McpToolsListTests(ITestOutputHelper output) => _output = output;

    [SkippableFact]
    public async Task ToolsList_ReturnsNonEmptyToolset()
    {
        Skip.IfNot(SmokeEnvironment.HasFullCredentials, "Smoke credentials not set");
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        using var client = SmokeHttpClient.Create(baseUri);

        var accessToken = await AcquireAccessTokenAsync(client);
        _output.WriteLine($"access_token={SmokeEnvironment.Redact(accessToken)}");

        using var rpcResponse = await PostToolsListAsync(client, accessToken);
        rpcResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await rpcResponse.Content.ReadFromJsonAsync<JsonRpcResponse<JsonRpcToolsListResult>>();
        result.Should().NotBeNull();
        result!.JsonRpc.Should().Be("2.0");
        result.Result.Should().NotBeNull();
        result.Result!.Tools.Should().NotBeEmpty(
            because: "the upstream MS Remote MCP server exposes at least one tool");
    }

    /// <summary>
    /// Phase-0 compatibility diagnostic: captures the CURRENT upstream tools/list surface
    /// through the bridge and reports its shape, so we can confirm which of the bridge's
    /// upstream-shape assumptions are still valid after Microsoft's tool consolidation —
    /// specifically whether any tool advertises a bare <c>{"type":"object"}</c> schema, which
    /// tools became <c>action</c> dispatchers, and whether the bridge's own tools still carry
    /// full schemas. Emits a summary to test output and dumps the raw surface to disk.
    /// </summary>
    [SkippableFact]
    public async Task ToolsList_DumpsUpstreamSurface_ForDiagnostics()
    {
        Skip.IfNot(SmokeEnvironment.HasFullCredentials, "Smoke credentials not set");
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        using var client = SmokeHttpClient.Create(baseUri);

        var accessToken = await AcquireAccessTokenAsync(client);

        using var rpcResponse = await PostToolsListAsync(client, accessToken);
        rpcResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await rpcResponse.Content.ReadAsStringAsync();
        var report = UpstreamToolsListAnalyzer.Analyze(body);

        _output.WriteLine(report.ToText());
        _output.WriteLine(UpstreamSurfaceDump.Write(body, report));

        // Regression guards on the bridge's own contract: the native tools must be advertised
        // and must carry a real schema (not the bare-object shape that makes clients guess
        // parameter names). Upstream tool shapes are reported, not asserted — they are what we
        // are here to observe.
        var writeField = report.Find("ado_bridge_write_field_from_slot");
        writeField.Should().NotBeNull(
            because: "the bridge must still advertise its native field-write tool");
        writeField!.HasProperties.Should().BeTrue(
            because: "the native tool must publish a real parameter schema, not {\"type\":\"object\"}");
    }

    private static async Task<string> AcquireAccessTokenAsync(HttpClient client)
    {
        var refreshToken = SmokeEnvironment.RequireRefreshToken();
        var clientId = SmokeEnvironment.RequireClientId();

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("client_id", clientId),
        });
        using var tokenResponse = await client.PostAsync("/token", form);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        token.Should().NotBeNull();
        return token!.AccessToken;
    }

    private static async Task<HttpResponseMessage> PostToolsListAsync(HttpClient client, string accessToken)
    {
        var rpc = new JsonRpcRequest("2.0", 1, "tools/list", new { });
        using var rpcRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(rpc),
        };
        rpcRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(rpcRequest);
    }
}
