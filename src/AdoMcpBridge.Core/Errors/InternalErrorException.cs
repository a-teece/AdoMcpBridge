namespace AdoMcpBridge.Core.Errors;

public sealed class InternalErrorException : BridgeException
{
    public InternalErrorException(string internalMessage, Exception? inner = null)
        : base("internal_error", internalMessage, inner) { }

    public override int StatusCode => 500;
}
