using AdoMcpBridge.Core.Entra;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class EntraAuthExceptionTests
{
    [Fact]
    public void Carries_error_code_and_status_without_secret_data()
    {
        var ex = new EntraAuthException(
            EntraAuthFailure.RefreshRejected,
            statusCode: 401,
            entraErrorCode: "invalid_grant",
            message: "Entra rejected the refresh token.");

        ex.Failure.Should().Be(EntraAuthFailure.RefreshRejected);
        ex.StatusCode.Should().Be(401);
        ex.EntraErrorCode.Should().Be("invalid_grant");
        ex.Message.Should().Be("Entra rejected the refresh token.");
    }

    [Fact]
    public void Preserves_inner_exception()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new EntraAuthException(EntraAuthFailure.Transport, null, null, "transport failed", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}
