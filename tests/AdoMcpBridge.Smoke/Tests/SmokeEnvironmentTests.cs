using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("Category", "smoke")]
public class SmokeEnvironmentTests
{
    [Fact]
    public void Redact_HidesTokenValueButKeepsLengthHint()
    {
        var redacted = SmokeEnvironment.Redact("abcdefghijklmnop");
        redacted.Should().Be("<redacted len=16>");
        redacted.Should().NotContain("abc");
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsSentinel()
    {
        SmokeEnvironment.Redact(null).Should().Be("<redacted len=0>");
        SmokeEnvironment.Redact("").Should().Be("<redacted len=0>");
    }
}
