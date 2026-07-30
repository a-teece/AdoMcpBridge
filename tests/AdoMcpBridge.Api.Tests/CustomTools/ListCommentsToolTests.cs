using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class ListCommentsToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private ListCommentsTool CreateTool() => new(_ado, NullLogger<ListCommentsTool>.Instance);

    private static JsonElement Args(string org = "org", string project = "proj", int workItemId = 42) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { organization = org, project, workItemId }))
            .RootElement.Clone();

    private static JsonElement Comment(int id, string text) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            id,
            text,
            createdBy = new { displayName = "Author " + id },
            createdDate = "2026-07-01T10:00:00Z",
        })).RootElement.Clone();

    [Fact]
    public async Task Returns_metadata_only_never_the_full_bodies()
    {
        var big = new string('x', 9000);
        _ado.GetWorkItemCommentsAsync("org", "proj", 42, Arg.Any<CancellationToken>())
            .Returns(new[] { Comment(1, "short"), Comment(2, big) });

        var result = await CreateTool().InvokeAsync(Args(), default);

        result.IsError.Should().BeFalse();
        result.Text.Should().NotContain(big); // full body never inlined
        var root = JsonDocument.Parse(result.Text).RootElement;
        root.GetProperty("count").GetInt32().Should().Be(2);
        var second = root.GetProperty("value")[1];
        second.GetProperty("id").GetInt32().Should().Be(2);
        second.GetProperty("charCount").GetInt32().Should().Be(9000);
        second.TryGetProperty("text", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Returns_empty_list_when_no_comments()
    {
        _ado.GetWorkItemCommentsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<JsonElement>());

        var result = await CreateTool().InvokeAsync(Args(), default);

        JsonDocument.Parse(result.Text).RootElement.GetProperty("count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Returns_error_on_ado_http_failure()
    {
        _ado.GetWorkItemCommentsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JsonElement>>(_ => throw new HttpRequestException("500"));

        var result = await CreateTool().InvokeAsync(Args(), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed");
    }
}
