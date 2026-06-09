namespace AdoMcpBridge.Core.Errors;

public sealed class UpstreamErrorException : BridgeException
{
    public UpstreamErrorException(string description, int mappedStatusCode = 502, Exception? inner = null)
        : base("upstream_error", description, inner)
    {
        StatusCode = mappedStatusCode;
    }

    public override int StatusCode { get; }
}
