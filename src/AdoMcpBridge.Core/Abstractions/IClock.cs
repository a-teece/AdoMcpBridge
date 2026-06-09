namespace AdoMcpBridge.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
