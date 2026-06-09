using AdoMcpBridge.Api.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace AdoMcpBridge.Api.Tests.Telemetry;

public class TelemetryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBridgeTelemetry_Registers_MeterProvider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationInsights:ConnectionString"] = "" // empty disables exporter, keeps wiring testable
            })
            .Build();

        services.AddLogging();
        services.AddBridgeTelemetry(config);

        using var provider = services.BuildServiceProvider();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }
}
