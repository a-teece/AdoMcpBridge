using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class ApprovalsListToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private ApprovalsListTool CreateTool() => new(_ado, NullLogger<ApprovalsListTool>.Instance);

    private static JsonElement Args(object o)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(o));
        return doc.RootElement.Clone();
    }

    private static JsonElement Approval(string id, string status)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            id,
            status,
            executionOrder = "anyOrder",
            minRequiredApprovers = 1,
            createdOn = "x",
            lastModifiedOn = "y",
        }));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task InvokeAsync_returns_count_and_projected_value_array()
    {
        _ado.QueryApprovalsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([Approval("a1", "pending"), Approval("a2", "approved")]);

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", project = "proj" }), default);

        result.IsError.Should().BeFalse();
        var root = JsonDocument.Parse(result.Text).RootElement;
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("value").GetArrayLength().Should().Be(2);
        root.GetProperty("value")[0].GetProperty("id").GetString().Should().Be("a1");
    }

    [Fact]
    public async Task InvokeAsync_forwards_all_optional_filters_to_the_client()
    {
        _ado.QueryApprovalsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateTool().InvokeAsync(Args(new
        {
            organization = "org",
            project = "proj",
            state = "pending",
            approvalIds = new[] { "a1", "a2" },
            userIds = new[] { "u1" },
            top = 25,
            expand = "permissions",
        }), default);

        await _ado.Received(1).QueryApprovalsAsync(
            "org", "proj",
            Arg.Is<IReadOnlyList<string>?>(v => v != null && v.SequenceEqual(new[] { "a1", "a2" })),
            "pending",
            Arg.Is<IReadOnlyList<string>?>(v => v != null && v.SequenceEqual(new[] { "u1" })),
            25, "permissions", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_defaults_expand_to_steps_and_passes_null_filters_when_omitted()
    {
        _ado.QueryApprovalsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateTool().InvokeAsync(Args(new { organization = "org", project = "proj" }), default);

        await _ado.Received(1).QueryApprovalsAsync(
            "org", "proj", null, null, null, null, "steps", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_returns_error_on_ado_http_failure()
    {
        _ado.QueryApprovalsAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JsonElement>>(_ => throw new HttpRequestException("403 Forbidden"));

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", project = "proj" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed");
    }
}
