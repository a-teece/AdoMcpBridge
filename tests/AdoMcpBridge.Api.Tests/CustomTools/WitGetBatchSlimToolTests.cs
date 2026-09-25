using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitGetBatchSlimToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();
    private readonly IWorkItemFieldTypeCache _cache = Substitute.For<IWorkItemFieldTypeCache>();

    private WitGetBatchSlimTool CreateTool() =>
        new(_ado, _cache, NullLogger<WitGetBatchSlimTool>.Instance);

    [Fact]
    public async Task InvokeAsync_returns_empty_array_for_empty_ids()
    {
        var result = await CreateTool().InvokeAsync(MakeArgs("org", "proj"), default);

        result.IsError.Should().BeFalse();
        result.Text.Should().Be("[]");
    }

    [Fact]
    public async Task InvokeAsync_throws_caller_argument_exception_when_ids_absent()
    {
        var args = JsonDocument.Parse("{\"organization\":\"org\",\"project\":\"proj\"}").RootElement.Clone();

        var act = () => CreateTool().InvokeAsync(args, default);

        await act.Should().ThrowAsync<CallerArgumentException>()
            .WithMessage("'ids' is required and must be an array of integers.");
    }

    [Fact]
    public async Task InvokeAsync_throws_caller_argument_exception_when_ids_not_an_array()
    {
        var args = JsonDocument.Parse("{\"organization\":\"org\",\"project\":\"proj\",\"ids\":42}")
            .RootElement.Clone();

        var act = () => CreateTool().InvokeAsync(args, default);

        await act.Should().ThrowAsync<CallerArgumentException>()
            .WithMessage("'ids' is required and must be an array of integers.");
    }

    [Fact]
    public async Task InvokeAsync_throws_caller_argument_exception_when_organization_omitted()
    {
        var args = JsonDocument.Parse("{\"project\":\"proj\",\"ids\":[1]}").RootElement.Clone();

        var act = () => CreateTool().InvokeAsync(args, default);

        await act.Should().ThrowAsync<CallerArgumentException>()
            .WithMessage("'organization' is required and must be a non-empty string.");
    }

    [Fact]
    public async Task InvokeAsync_throws_caller_argument_exception_when_ids_contains_a_non_integer_element()
    {
        var args = JsonDocument.Parse("{\"organization\":\"org\",\"project\":\"proj\",\"ids\":[\"a\"]}")
            .RootElement.Clone();

        var act = () => CreateTool().InvokeAsync(args, default);

        await act.Should().ThrowAsync<CallerArgumentException>()
            .WithMessage("'ids' must be an array of integers.");
    }

    [Fact]
    public async Task InvokeAsync_returns_error_when_ids_exceed_200()
    {
        var ids = Enumerable.Range(1, 201).ToArray();
        var result = await CreateTool().InvokeAsync(MakeArgs("org", "proj", ids), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("200");
    }

    [Fact]
    public async Task InvokeAsync_returns_slim_json_array_for_batch()
    {
        var wis = new[]
        {
            MakeWorkItem(1, new Dictionary<string, object?> { ["System.Title"] = "Bug A", ["System.Description"] = "<p>Long</p>" }),
            MakeWorkItem(2, new Dictionary<string, object?> { ["System.Title"] = "Bug B", ["System.Description"] = null }),
        };
        _ado.GetWorkItemsBatchAsync("org", "proj", Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns(wis.ToList());
        _cache.GetLongTextFieldRefNamesAsync("org", Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(["System.Description"]));

        var result = await CreateTool().InvokeAsync(MakeArgs("org", "proj", 1, 2), default);

        result.IsError.Should().BeFalse();
        var arr = JsonDocument.Parse(result.Text).RootElement.EnumerateArray().ToList();
        arr.Should().HaveCount(2);

        // First item: description stubbed
        arr[0].GetProperty("fields").GetProperty("System.Title").GetString().Should().Be("Bug A");
        arr[0].GetProperty("fields").GetProperty("System.Description")
            .TryGetProperty("charCount", out _).Should().BeTrue();

        // Second item: null description passed through unchanged
        arr[1].GetProperty("fields").GetProperty("System.Title").GetString().Should().Be("Bug B");
        arr[1].GetProperty("fields").GetProperty("System.Description").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task InvokeAsync_surfaces_ado_status_and_message_on_non_success()
    {
        _ado.GetWorkItemsBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JsonElement>>(_ => throw new AdoRestException(503, "service unavailable"));
        _cache.GetLongTextFieldRefNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        var result = await CreateTool().InvokeAsync(MakeArgs("org", "proj", 1, 2), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 503");
        result.Text.Should().Contain("service unavailable");
    }

    [Fact]
    public async Task InvokeAsync_returns_transport_error_on_transport_failure()
    {
        _ado.GetWorkItemsBatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JsonElement>>(_ => throw new HttpRequestException("connection reset"));
        _cache.GetLongTextFieldRefNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        var result = await CreateTool().InvokeAsync(MakeArgs("org", "proj", 1, 2), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed (transport)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JsonElement MakeWorkItem(int id, Dictionary<string, object?> fields)
    {
        var json = JsonSerializer.Serialize(new
        {
            id,
            rev = 1,
            url = $"https://dev.azure.com/test/proj/_apis/wit/workitems/{id}",
            fields,
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement MakeArgs(string org, string project, params int[] ids)
    {
        using var doc = JsonDocument.Parse(
            JsonSerializer.Serialize(new { organization = org, project, ids }));
        return doc.RootElement.Clone();
    }
}
