using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class ToolArgsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ── RequireString ─────────────────────────────────────────────────────────

    [Fact]
    public void RequireString_returns_the_value_when_present()
    {
        ToolArgs.RequireString(Parse("{\"organization\":\"my-org\"}"), "organization")
            .Should().Be("my-org");
    }

    [Theory]
    [InlineData("{}")]                              // absent
    [InlineData("{\"organization\":null}")]          // JSON null
    [InlineData("{\"organization\":42}")]            // wrong type
    [InlineData("{\"organization\":\"\"}")]          // empty
    [InlineData("{\"organization\":\"   \"}")]       // whitespace
    public void RequireString_throws_when_missing_null_wrong_type_or_blank(string json)
    {
        var act = () => ToolArgs.RequireString(Parse(json), "organization");

        act.Should().Throw<CallerArgumentException>()
            .WithMessage("'organization' is required and must be a non-empty string.");
    }

    [Fact]
    public void RequireString_throws_when_arguments_is_not_an_object()
    {
        var act = () => ToolArgs.RequireString(default, "organization");

        act.Should().Throw<CallerArgumentException>()
            .WithMessage("'organization' is required and must be a non-empty string.");
    }

    // ── RequireInt ────────────────────────────────────────────────────────────

    [Fact]
    public void RequireInt_returns_the_value_when_present()
    {
        ToolArgs.RequireInt(Parse("{\"workItemId\":42}"), "workItemId").Should().Be(42);
    }

    [Theory]
    [InlineData("{}")]                               // absent
    [InlineData("{\"workItemId\":null}")]             // JSON null
    [InlineData("{\"workItemId\":\"42\"}")]           // string, not number
    [InlineData("{\"workItemId\":1.5}")]              // non-integer number
    public void RequireInt_throws_when_missing_null_or_not_an_integer(string json)
    {
        var act = () => ToolArgs.RequireInt(Parse(json), "workItemId");

        act.Should().Throw<CallerArgumentException>()
            .WithMessage("'workItemId' is required and must be an integer.");
    }

    // ── GetString ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetString_returns_the_value_when_present()
    {
        ToolArgs.GetString(Parse("{\"project\":\"proj\"}"), "project").Should().Be("proj");
    }

    [Theory]
    [InlineData("{}")]                       // absent
    [InlineData("{\"project\":null}")]        // JSON null
    [InlineData("{\"project\":42}")]          // wrong type
    public void GetString_returns_null_when_absent_null_or_wrong_type(string json)
    {
        ToolArgs.GetString(Parse(json), "project").Should().BeNull();
    }

    [Fact]
    public void GetString_returns_null_when_arguments_is_not_an_object()
    {
        ToolArgs.GetString(default, "project").Should().BeNull();
    }

    // ── GetInt ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetInt_returns_the_value_when_present()
    {
        ToolArgs.GetInt(Parse("{\"top\":50}"), "top").Should().Be(50);
    }

    [Theory]
    [InlineData("{}")]                // absent
    [InlineData("{\"top\":null}")]    // JSON null
    public void GetInt_returns_null_when_absent_or_null(string json)
    {
        ToolArgs.GetInt(Parse(json), "top").Should().BeNull();
    }

    [Theory]
    [InlineData("{\"top\":\"50\"}")]  // string, not number
    [InlineData("{\"top\":1.5}")]     // non-integer number
    public void GetInt_throws_when_present_but_not_an_integer(string json)
    {
        var act = () => ToolArgs.GetInt(Parse(json), "top");

        act.Should().Throw<CallerArgumentException>()
            .WithMessage("'top' must be an integer.");
    }
}
