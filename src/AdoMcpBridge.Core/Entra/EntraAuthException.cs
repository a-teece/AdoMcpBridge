namespace AdoMcpBridge.Core.Entra;

public enum EntraAuthFailure
{
    AuthorizationCodeRejected,
    RefreshRejected,
    CertificateUnavailable,
    Transport,
    Unknown,
}

public sealed class EntraAuthException : Exception
{
    public EntraAuthFailure Failure { get; }
    public int? StatusCode { get; }
    public string? EntraErrorCode { get; }

    public EntraAuthException(
        EntraAuthFailure failure,
        int? statusCode,
        string? entraErrorCode,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        StatusCode = statusCode;
        EntraErrorCode = entraErrorCode;
    }
}
