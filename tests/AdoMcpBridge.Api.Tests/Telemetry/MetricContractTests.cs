using System.Diagnostics.Metrics;
using AdoMcpBridge.Api.Telemetry;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.Telemetry;

public class MetricContractTests
{
    private static readonly string[] ExpectedInstruments =
    {
        "oauth.dcr.registrations",
        "oauth.token.issued",
        "oauth.token.refreshed",
        "oauth.token.rejected",
        "entra.refresh.duration_ms",
        "proxy.upstream.duration_ms",
        "proxy.upstream.errors",
        "proxy.in_flight",
    };

    [Fact]
    public void All_Contract_Instruments_Are_Published_On_The_AdoMcpBridge_Meter()
    {
        _ = BridgeMeter.MeterName;

        var seen = new HashSet<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, _) =>
            {
                if (inst.Meter.Name == BridgeMeter.MeterName) seen.Add(inst.Name);
            }
        };
        listener.Start();

        seen.Should().BeEquivalentTo(ExpectedInstruments);
    }
}
