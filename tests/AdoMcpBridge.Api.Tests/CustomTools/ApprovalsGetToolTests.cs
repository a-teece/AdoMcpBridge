using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class ApprovalsGetToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private ApprovalsGetTool CreateTool() => new(_ado, NullLogger<ApprovalsGetTool>.Instance);

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
    public async Task InvokeAsync_returns_projected_approval_and_defaults_expand_to_steps()
    {
        _ado.GetApprovalAsync("org", "proj", "a1", "steps", Arg.Any<CancellationToken>())
            .Returns(Approval("a1", "pending"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", approvalId = "a1" }), default);

        result.IsError.Should().BeFalse();
        var root = JsonDocument.Parse(result.Text).RootElement;
        root.GetProperty("id").GetString().Should().Be("a1");
        root.GetProperty("status").GetString().Should().Be("pending");
    }

    [Fact]
    public async Task InvokeAsync_forwards_explicit_expand()
    {
        _ado.GetApprovalAsync("org", "proj", "a1", "none", Arg.Any<CancellationToken>())
            .Returns(Approval("a1", "pending"));

        await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", approvalId = "a1", expand = "none" }), default);

        await _ado.Received(1).GetApprovalAsync("org", "proj", "a1", "none", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_returns_error_when_not_found()
    {
        _ado.GetApprovalAsync("org", "proj", "missing", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((JsonElement?)null);

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", approvalId = "missing" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("not found");
    }

    [Fact]
    public async Task InvokeAsync_returns_error_on_ado_http_failure()
    {
        _ado.GetApprovalAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<JsonElement?>(_ => throw new HttpRequestException("500"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", approvalId = "a1" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed");
    }
}
