using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using AdoMcpBridge.Core.BlobStorage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class AddCommentToolTests
{
    private readonly IBlobSlotStore _blobs = Substitute.For<IBlobSlotStore>();
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private AddCommentTool CreateTool() => new(_blobs, _ado, NullLogger<AddCommentTool>.Instance);

    private static JsonElement Args(Dictionary<string, object?> extra)
    {
        var props = new Dictionary<string, object?>
        {
            ["organization"] = "org",
            ["project"] = "proj",
            ["workItemId"] = 42,
        };
        foreach (var kv in extra) props[kv.Key] = kv.Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(props)).RootElement.Clone();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void StubCreatedComment(int id = 99) =>
        _ado.AddWorkItemCommentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(JsonDocument.Parse(JsonSerializer.Serialize(new { id })).RootElement.Clone());

    // ── inline path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Posts_small_inline_comment_and_returns_added_status()
    {
        StubCreatedComment(101);

        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = "looks good" }), default);

        result.IsError.Should().BeFalse();
        var root = JsonDocument.Parse(result.Text).RootElement;
        root.GetProperty("status").GetString().Should().Be("ADDED");
        root.GetProperty("commentId").GetInt32().Should().Be(101);
        root.GetProperty("charCount").GetInt32().Should().Be("looks good".Length);
        await _ado.Received(1).AddWorkItemCommentAsync("org", "proj", 42, "looks good", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_oversized_inline_comment_and_does_not_post()
    {
        var big = new string('x', AddCommentTool.InlineCommentCharLimit + 1);

        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = big }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ado_bridge_create_upload_slot");
        await _ado.DidNotReceive().AddWorkItemCommentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── slot path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Posts_comment_from_slot_when_sha_matches_then_deletes_slot()
    {
        var body = new string('y', AddCommentTool.InlineCommentCharLimit + 500);
        var bytes = Encoding.UTF8.GetBytes(body);
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(bytes);
        StubCreatedComment(202);

        var result = await CreateTool().InvokeAsync(
            Args(new() { ["slotId"] = "slot-1", ["sha256"] = Sha(bytes) }), default);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement.GetProperty("commentId").GetInt32().Should().Be(202);
        await _ado.Received(1).AddWorkItemCommentAsync("org", "proj", 42, body, Arg.Any<CancellationToken>());
        await _blobs.Received(1).DeleteSlotAsync("slot-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_slot_body_on_sha_mismatch_and_does_not_post()
    {
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Encoding.UTF8.GetBytes("actual"));

        var result = await CreateTool().InvokeAsync(
            Args(new() { ["slotId"] = "slot-1", ["sha256"] = Sha(Encoding.UTF8.GetBytes("expected-different")) }),
            default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("SHA-256 mismatch");
        await _ado.DidNotReceive().AddWorkItemCommentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Requires_sha256_when_posting_from_a_slot()
    {
        var result = await CreateTool().InvokeAsync(Args(new() { ["slotId"] = "slot-1" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("sha256");
    }

    // ── argument validation ───────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_when_both_text_and_slot_supplied()
    {
        var result = await CreateTool().InvokeAsync(
            Args(new() { ["text"] = "x", ["slotId"] = "slot-1" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("exactly one");
    }

    [Fact]
    public async Task Rejects_when_neither_text_nor_slot_supplied()
    {
        var result = await CreateTool().InvokeAsync(Args(new()), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("exactly one");
    }

    [Fact]
    public async Task Returns_error_on_ado_http_failure()
    {
        _ado.AddWorkItemCommentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<JsonElement>(_ => throw new HttpRequestException("500"));

        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = "hi" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed");
    }
}
