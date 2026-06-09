using AdoMcpBridge.Core.Errors;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.Errors;

public class InternalErrorExceptionTests
{
    [Fact]
    public void Carries_Opaque_ErrorId_And_500()
    {
        var ex = new InternalErrorException("db unreachable");
        ex.StatusCode.Should().Be(500);
        ex.ErrorCode.Should().Be("internal_error");
        ex.ErrorId.Should().HaveLength(32); // Guid "n" format
    }
}
