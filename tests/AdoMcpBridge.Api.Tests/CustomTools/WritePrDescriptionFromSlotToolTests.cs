using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using AdoMcpBridge.Core.BlobStorage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WritePrDescriptionFromSlotToolTests
{
    private readonly IBlobSlotStore _blobs = Substitute.For<IBlobSlotStore>();
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private WritePrDescriptionFromSlotTool CreateTool() =>
        new(_blobs, _ado, NullLogger<WritePrDescriptionFromSlotTool>.Instance);

    private static JsonElement Args(
        string org = "myorg",
        string project = "myproject",
        string repositoryId = "myrepo",
        int pullRequestId = 77,
        string slotId = "slot-1",
        string? sha256 = null,
        string content = "Some PR description",
        bool omitSha256 = false)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256 ?? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var props = new Dictionary<string, object?>
        {
            ["organization"] = org,
            ["project"] = project,
            ["repositoryId"] = repositoryId,
            ["pullRequestId"] = pullRequestId,
            ["slotId"] = slotId,
        };
        if (!omitSha256) props["sha256"] = hash;

        return JsonDocument.Parse(JsonSerializer.Serialize(props)).RootElement.Clone();
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    // ── happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsWrittenStatusAndCharCount_AndPatchesTheDescription()
    {
        const string desc = "A concise pull-request description.";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(desc));

        var result = await CreateTool().InvokeAsync(Args(content: desc), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("status").GetString().Should().Be("WRITTEN");
        doc.GetProperty("charCount").GetInt32().Should().Be(desc.Length);

        await _ado.Received(1).UpdatePullRequestDescriptionAsync(
            "myorg", "myproject", "myrepo", 77, desc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeletesSlot_AfterSuccessfulWrite()
    {
        const string desc = "data";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(desc));

        await CreateTool().InvokeAsync(Args(content: desc), CancellationToken.None);

        await _blobs.Received(1).DeleteSlotAsync("slot-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteSucceeds_EvenWhenSlotDeleteFails()
    {
        const string desc = "data";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(desc));
        _blobs.DeleteSlotAsync("slot-1", Arg.Any<CancellationToken>())
              .ThrowsAsync(new InvalidOperationException("delete failed"));

        var result = await CreateTool().InvokeAsync(Args(content: desc), CancellationToken.None);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement.GetProperty("status").GetString().Should().Be("WRITTEN");
    }

    // ── length limit ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AllowsDescription_AtExactly4000Units()
    {
        var desc = new string('a', 4000);
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(desc));

        var result = await CreateTool().InvokeAsync(Args(content: desc), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("status").GetString().Should().Be("WRITTEN");
        doc.GetProperty("charCount").GetInt32().Should().Be(4000);
        await _ado.Received(1).UpdatePullRequestDescriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            desc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsError_WhenDescriptionExceedsLimit_WithoutCallingAdoOrDeletingSlot()
    {
        var desc = new string('a', 4001);
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(desc));

        var result = await CreateTool().InvokeAsync(Args(content: desc), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("4001 UTF-16 units");
        result.Text.Should().Contain("over the 4000 limit");

        await _ado.DidNotReceive().UpdatePullRequestDescriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobs.DidNotReceive().DeleteSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── SHA-256 validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsSha256MismatchError_WhenHashDoesNotMatch()
    {
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Utf8("actual content"));

        // sha256 is computed from "different content", not "actual content".
        var result = await CreateTool().InvokeAsync(Args(content: "different content"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("SHA-256 mismatch");
        await _ado.DidNotReceive().UpdatePullRequestDescriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── slot read failure ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsError_WhenSlotReadFails()
    {
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .ThrowsAsync(new InvalidOperationException("blob not found"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("Failed to read upload slot");
    }

    // ── ADO failures ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsError_SurfacingStatusAndMessage_OnAdoRestException()
    {
        const string desc = "data";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(desc));
        _ado.UpdatePullRequestDescriptionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AdoRestException(404, "pull request does not exist"));

        var result = await CreateTool().InvokeAsync(Args(content: desc), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 404");
        result.Text.Should().Contain("pull request does not exist");
        await _blobs.DidNotReceive().DeleteSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsError_OnTransportFailure()
    {
        const string desc = "data";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(desc));
        _ado.UpdatePullRequestDescriptionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(Args(content: desc), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("transport");
    }

    // ── argument validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_WhenRequiredArgMissing()
    {
        var act = () => CreateTool().InvokeAsync(Args(omitSha256: true), CancellationToken.None);

        await act.Should().ThrowAsync<CallerArgumentException>();
    }
}
