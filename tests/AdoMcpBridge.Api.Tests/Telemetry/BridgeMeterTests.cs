using System.Diagnostics.Metrics;
using AdoMcpBridge.Api.Telemetry;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.Telemetry;

public class BridgeMeterTests
{
    [Fact]
    public void Meter_Name_Is_AdoMcpBridge()
    {
        BridgeMeter.MeterName.Should().Be("AdoMcpBridge");
        BridgeMeter.Meter.Name.Should().Be("AdoMcpBridge");
    }

    [Fact]
    public void TokenIssued_Counter_Records_With_GrantType_Tag()
    {
        var captured = new List<(string instrument, long value, KeyValuePair<string, object?>[] tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == "AdoMcpBridge") l.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
            captured.Add((inst.Name, value, tags.ToArray())));
        listener.Start();

        BridgeMeter.TokenIssued.Add(1, new KeyValuePair<string, object?>("grant_type", "authorization_code"));

        captured.Should().ContainSingle(c =>
            c.instrument == "oauth.token.issued"
            && c.value == 1
            && c.tags.Any(t => t.Key == "grant_type" && (string)t.Value! == "authorization_code"));
    }
}
