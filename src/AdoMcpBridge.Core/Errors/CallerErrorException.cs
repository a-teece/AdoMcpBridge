namespace AdoMcpBridge.Core.Errors;

public sealed class CallerErrorException : BridgeException
{
    public CallerErrorException(string errorCode, string description, Exception? inner = null)
        : base(errorCode, description, inner) { }

    public override int StatusCode => 400;
}
