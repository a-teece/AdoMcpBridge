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

    // Base args carry a valid 'format' by default so tests exercising other behaviour clear the
    // format gate; put ["format"] in the extra dict (or use ArgsWithoutFormat) to override it.
    private static JsonElement Args(Dictionary<string, object?> extra)
    {
        var props = new Dictionary<string, object?>
        {
            ["organization"] = "org",
            ["project"] = "proj",
            ["workItemId"] = 42,
            ["format"] = "markdown",
        };
        foreach (var kv in extra) props[kv.Key] = kv.Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(props)).RootElement.Clone();
    }

    private static JsonElement ArgsWithoutFormat(Dictionary<string, object?> extra)
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
        _ado.AddWorkItemCommentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(JsonDocument.Parse(JsonSerializer.Serialize(new { id })).RootElement.Clone());

    // ── format is required and must be explicit ────────────────────────────────

    [Fact]
    public async Task Rejects_when_format_omitted_and_does_not_post()
    {
        var result = await CreateTool().InvokeAsync(
            ArgsWithoutFormat(new() { ["text"] = "hi" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("format is required");
        result.Text.Should().Contain("no default is assumed");
        await AssertNothingPosted();
    }

    [Fact]
    public async Task Rejects_when_format_is_json_null_and_does_not_post()
    {
        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = "hi", ["format"] = null }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("format is required");
        await AssertNothingPosted();
    }

    [Fact]
    public async Task Rejects_when_format_is_empty_string_and_does_not_post()
    {
        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = "hi", ["format"] = "" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("format is required");
        await AssertNothingPosted();
    }

    [Fact]
    public async Task Rejects_when_format_is_unrecognised_and_does_not_post()
    {
        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = "hi", ["format"] = "md" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("format is required");
        result.Text.Should().Contain("md");
        await AssertNothingPosted();
    }

    private async Task AssertNothingPosted()
    {
        await _ado.DidNotReceive().AddWorkItemCommentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _blobs.DidNotReceive().ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── format routing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Markdown_format_posts_as_markdown_and_reports_it()
    {
        StubCreatedComment(101);

        var result = await CreateTool().InvokeAsync(
            Args(new() { ["text"] = "**bold**", ["format"] = "markdown" }), default);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement.GetProperty("format").GetString().Should().Be("markdown");
        await _ado.Received(1).AddWorkItemCommentAsync("org", "proj", 42, "**bold**", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Html_format_posts_as_html_and_reports_it()
    {
        StubCreatedComment(102);

        var result = await CreateTool().InvokeAsync(
            Args(new() { ["text"] = "<b>bold</b>", ["format"] = "html" }), default);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement.GetProperty("format").GetString().Should().Be("html");
        await _ado.Received(1).AddWorkItemCommentAsync("org", "proj", 42, "<b>bold</b>", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Format_is_case_insensitive()
    {
        StubCreatedComment(103);

        var result = await CreateTool().InvokeAsync(
            Args(new() { ["text"] = "hi", ["format"] = "MARKDOWN" }), default);

        result.IsError.Should().BeFalse();
        await _ado.Received(1).AddWorkItemCommentAsync("org", "proj", 42, "hi", true, Arg.Any<CancellationToken>());
    }

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
        await _ado.Received(1).AddWorkItemCommentAsync("org", "proj", 42, "looks good", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_oversized_inline_comment_and_does_not_post()
    {
        var big = new string('x', AddCommentTool.InlineCommentCharLimit + 1);

        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = big }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ado_bridge_create_upload_slot");
        await _ado.DidNotReceive().AddWorkItemCommentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
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
        await _ado.Received(1).AddWorkItemCommentAsync("org", "proj", 42, body, true, Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
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
    public async Task Returns_error_surfacing_ado_status_and_message_on_non_success()
    {
        _ado.AddWorkItemCommentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<JsonElement>(_ => throw new AdoRestException(404, "TF401232: work item 42 does not exist"));

        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = "hi" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 404");
        result.Text.Should().Contain("TF401232");
    }

    [Fact]
    public async Task Returns_transport_error_on_transport_failure()
    {
        _ado.AddWorkItemCommentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<JsonElement>(_ => throw new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(Args(new() { ["text"] = "hi" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed (transport)");
    }
}
