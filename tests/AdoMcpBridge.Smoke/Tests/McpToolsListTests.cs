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
        var refreshToken = SmokeEnvironment.RequireRefreshToken();
        var clientId = SmokeEnvironment.RequireClientId();
        using var client = SmokeHttpClient.Create(baseUri);

        // Refresh to a fresh access token first.
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
        _output.WriteLine($"access_token={SmokeEnvironment.Redact(token!.AccessToken)}");

        // Call MCP tools/list through the proxy.
        var rpc = new JsonRpcRequest("2.0", 1, "tools/list", new { });
        using var rpcRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(rpc),
        };
        rpcRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using var rpcResponse = await client.SendAsync(rpcRequest);
        rpcResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await rpcResponse.Content.ReadFromJsonAsync<JsonRpcResponse<JsonRpcToolsListResult>>();
        result.Should().NotBeNull();
        result!.JsonRpc.Should().Be("2.0");
        result.Result.Should().NotBeNull();
        result.Result!.Tools.Should().NotBeEmpty(
            because: "the upstream MS Remote MCP server exposes at least one tool");
    }
}
