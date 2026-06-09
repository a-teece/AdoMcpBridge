using AdoMcpBridge.Core.Errors;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.Errors;

public class BridgeExceptionTests
{
    private sealed class TestEx : BridgeException
    {
        public TestEx() : base("test_code", "msg") { }
    }

    [Fact]
    public void ErrorId_Is_Generated_And_Unique()
    {
        var a = new TestEx();
        var b = new TestEx();
        a.ErrorId.Should().NotBeNullOrWhiteSpace();
        a.ErrorId.Should().NotBe(b.ErrorId);
        a.ErrorCode.Should().Be("test_code");
        a.StatusCode.Should().Be(500); // base default
    }

    [Fact]
    public void Default_StatusCode_Is_500()
    {
        new TestEx().StatusCode.Should().Be(500);
    }
}
