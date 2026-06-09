using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AdoMcpBridge.Api.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddBridgeTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var aiConnection = configuration["ApplicationInsights:ConnectionString"];

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: "ado-mcp-bridge"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddMeter(BridgeMeter.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(aiConnection))
        {
            otel.UseAzureMonitor(o => o.ConnectionString = aiConnection);
        }

        return services;
    }
}
