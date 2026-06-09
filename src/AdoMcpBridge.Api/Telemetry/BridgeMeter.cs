using System.Diagnostics.Metrics;

namespace AdoMcpBridge.Api.Telemetry;

public static class BridgeMeter
{
    public const string MeterName = "AdoMcpBridge";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> DcrRegistrations =
        Meter.CreateCounter<long>("oauth.dcr.registrations", description: "OAuth dynamic client registrations.");

    public static readonly Counter<long> TokenIssued =
        Meter.CreateCounter<long>("oauth.token.issued", description: "Wrapper tokens issued.");

    public static readonly Counter<long> TokenRefreshed =
        Meter.CreateCounter<long>("oauth.token.refreshed", description: "Wrapper tokens refreshed.");

    public static readonly Counter<long> TokenRejected =
        Meter.CreateCounter<long>("oauth.token.rejected", description: "Wrapper tokens rejected.");

    public static readonly Histogram<double> EntraRefreshDurationMs =
        Meter.CreateHistogram<double>("entra.refresh.duration_ms", unit: "ms", description: "Entra refresh latency.");

    public static readonly Histogram<double> ProxyUpstreamDurationMs =
        Meter.CreateHistogram<double>("proxy.upstream.duration_ms", unit: "ms", description: "Upstream MCP latency.");

    public static readonly Counter<long> ProxyUpstreamErrors =
        Meter.CreateCounter<long>("proxy.upstream.errors", description: "Upstream MCP errors.");

    public static readonly UpDownCounter<long> ProxyInFlight =
        Meter.CreateUpDownCounter<long>("proxy.in_flight", description: "In-flight proxy requests.");
}
