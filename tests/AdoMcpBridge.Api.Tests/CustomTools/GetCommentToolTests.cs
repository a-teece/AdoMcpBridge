using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class GetCommentToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private GetCommentTool CreateTool() => new(_ado, NullLogger<GetCommentTool>.Instance);

    private static JsonElement Args(int commentId = 7) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            organization = "org",
            project = "proj",
            workItemId = 42,
            commentId,
        })).RootElement.Clone();

    private static JsonElement Comment(string text) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { id = 7, text })).RootElement.Clone();

    [Fact]
    public async Task Returns_full_body_unescaped()
    {
        _ado.GetWorkItemCommentAsync("org", "proj", 42, 7, Arg.Any<CancellationToken>())
            .Returns(Comment("&lt;p&gt;hello &amp; goodbye&lt;/p&gt;"));

        var result = await CreateTool().InvokeAsync(Args(), default);

        result.IsError.Should().BeFalse();
        result.Text.Should().Be("<p>hello & goodbye</p>");
    }

    [Fact]
    public async Task Returns_error_when_comment_not_found()
    {
        _ado.GetWorkItemCommentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)null);

        var result = await CreateTool().InvokeAsync(Args(999), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("not found");
    }

    [Fact]
    public async Task Returns_error_on_ado_http_failure()
    {
        _ado.GetWorkItemCommentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<JsonElement?>(_ => throw new HttpRequestException("403"));

        var result = await CreateTool().InvokeAsync(Args(), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed");
    }
}
