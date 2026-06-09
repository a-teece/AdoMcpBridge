using AdoMcpBridge.Core.Errors;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.Errors;

public class UpstreamErrorExceptionTests
{
    [Fact]
    public void Default_StatusCode_Is_502()
    {
        var ex = new UpstreamErrorException("upstream timeout");
        ex.StatusCode.Should().Be(502);
        ex.ErrorCode.Should().Be("upstream_error");
    }

    [Fact]
    public void Mapped_StatusCode_Is_Preserved()
    {
        var ex = new UpstreamErrorException("rate limited", mappedStatusCode: 429);
        ex.StatusCode.Should().Be(429);
    }
}
