using AdoMcpBridge.Core.Errors;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.Errors;

public class CallerErrorExceptionTests
{
    [Fact]
    public void Carries_Oauth_Error_Fields()
    {
        var ex = new CallerErrorException("invalid_grant", "code expired");
        ex.ErrorCode.Should().Be("invalid_grant");
        ex.Message.Should().Be("code expired");
        ex.StatusCode.Should().Be(400);
    }
}
