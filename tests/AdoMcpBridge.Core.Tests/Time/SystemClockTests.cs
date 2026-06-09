using AdoMcpBridge.Core.Time;

namespace AdoMcpBridge.Core.Tests.Time;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_returns_value_within_one_second_of_DateTimeOffset_UtcNow()
    {
        IClock clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;
        var now = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.True(now >= before.AddSeconds(-1));
        Assert.True(now <= after.AddSeconds(1));
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }
}
