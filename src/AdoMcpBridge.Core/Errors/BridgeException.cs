namespace AdoMcpBridge.Core.Errors;

public abstract class BridgeException : Exception
{
    protected BridgeException(string errorCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        ErrorId = Guid.NewGuid().ToString("n");
    }

    public string ErrorCode { get; }
    public string ErrorId { get; }

    /// <summary>HTTP status this error maps to. Internal errors default to 500.</summary>
    public virtual int StatusCode => 500;
}
