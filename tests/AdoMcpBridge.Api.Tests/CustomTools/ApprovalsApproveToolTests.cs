using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class ApprovalsApproveToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private ApprovalsApproveTool CreateTool() => new(_ado, NullLogger<ApprovalsApproveTool>.Instance);

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
    public async Task InvokeAsync_sends_approved_status_and_comment_and_returns_projected_result()
    {
        _ado.UpdateApprovalsAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApprovalUpdate>>(), Arg.Any<CancellationToken>())
            .Returns([Approval("a1", "approved")]);

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", approvalId = "a1", comment = "ship it" }), default);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement.GetProperty("status").GetString().Should().Be("approved");

        await _ado.Received(1).UpdateApprovalsAsync(
            "org", "proj",
            Arg.Is<IReadOnlyList<ApprovalUpdate>>(u =>
                u.Count == 1 && u[0].ApprovalId == "a1" && u[0].Status == "approved" && u[0].Comment == "ship it"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_passes_null_comment_when_omitted()
    {
        _ado.UpdateApprovalsAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApprovalUpdate>>(), Arg.Any<CancellationToken>())
            .Returns([Approval("a1", "pending")]);

        await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", approvalId = "a1" }), default);

        await _ado.Received(1).UpdateApprovalsAsync(
            "org", "proj",
            Arg.Is<IReadOnlyList<ApprovalUpdate>>(u => u[0].Comment == null && u[0].Status == "approved"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_returns_error_when_update_returns_empty()
    {
        _ado.UpdateApprovalsAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApprovalUpdate>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", approvalId = "a1" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("no result");
    }

    [Fact]
    public async Task InvokeAsync_returns_error_on_ado_http_failure()
    {
        _ado.UpdateApprovalsAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApprovalUpdate>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<JsonElement>>(_ => throw new HttpRequestException("403 Forbidden"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", approvalId = "a1" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed");
    }
}
