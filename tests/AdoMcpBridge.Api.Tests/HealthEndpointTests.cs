namespace AdoMcpBridge.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _factory;

    public HealthEndpointTests(BridgeApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Healthz_returns_success_with_ok_body()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body, StringComparison.Ordinal);
    }
}
