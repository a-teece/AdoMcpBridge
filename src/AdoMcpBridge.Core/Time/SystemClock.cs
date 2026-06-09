using AdoMcpBridge.Core.Abstractions;

namespace AdoMcpBridge.Core.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
