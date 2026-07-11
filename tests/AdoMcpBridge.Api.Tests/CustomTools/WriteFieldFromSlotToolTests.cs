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

public class WriteFieldFromSlotToolTests
{
    private readonly IBlobSlotStore _blobs = Substitute.For<IBlobSlotStore>();
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private WriteFieldFromSlotTool CreateTool() =>
        new(_blobs, _ado, NullLogger<WriteFieldFromSlotTool>.Instance);

    private static JsonElement Args(
        string slotId = "slot-1",
        string org = "myorg",
        string project = "myproject",
        int workItemId = 42,
        string fieldRefName = "System.Description",
        string? sha256 = null,
        string? format = null,
        string content = "Hello world")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256 ?? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var props = new Dictionary<string, object?>
        {
            ["slotId"] = slotId,
            ["organization"] = org,
            ["project"] = project,
            ["workItemId"] = workItemId,
            ["fieldRefName"] = fieldRefName,
            ["sha256"] = hash,
        };
        if (format is not null) props["format"] = format;

        return JsonDocument.Parse(JsonSerializer.Serialize(props)).RootElement.Clone();
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    // ── html (default) ───────────────────────────────────────────────────────

    [Fact]
    public async Task Html_IsDefaultWhenFormatParamOmitted()
    {
        const string html = "<p>Hello</p>";
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Utf8(html));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(html);

        var result = await CreateTool().InvokeAsync(Args(content: html), CancellationToken.None);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement
            .GetProperty("status").GetString().Should().Be("WRITTEN");
    }

    [Fact]
    public async Task Html_SendsContentWithoutEntityEscaping()
    {
        const string html = "<h1>Title</h1><p>Hello &amp; world</p>";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(html));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(html);

        await CreateTool().InvokeAsync(Args(content: html, format: "html"), CancellationToken.None);

        await _ado.Received(1).PatchFieldAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            html,
            fieldFormat: null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Html_ReturnsWrittenStatusAndCharCount()
    {
        const string html = "<p>Content</p>";
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Utf8(html));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(html);

        var result = await CreateTool().InvokeAsync(Args(content: html, format: "html"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("status").GetString().Should().Be("WRITTEN");
        doc.GetProperty("charCount").GetInt32().Should().Be(html.Length);
    }

    [Fact]
    public async Task Html_IsNotErrorEvenWhenStoredValueDiffersFromSent()
    {
        // ADO may normalise HTML (e.g. encode quotes); WRITTEN status must not be affected.
        const string html = "<p>Say \"hello\"</p>";
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Utf8(html));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("<p>Say &quot;hello&quot;</p>");

        var result = await CreateTool().InvokeAsync(Args(content: html, format: "html"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement
            .GetProperty("status").GetString().Should().Be("WRITTEN");
    }

    // ── markdown format ───────────────────────────────────────────────────────

    [Fact]
    public async Task Markdown_SendsContentWithoutEntityEscapingAndDeclaresMarkdownFormat()
    {
        const string md = "# Title\n\n**bold** and `code`";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(md));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(md);

        await CreateTool().InvokeAsync(Args(content: md, format: "markdown"), CancellationToken.None);

        await _ado.Received(1).PatchFieldAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
            md,
            fieldFormat: "Markdown",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Markdown_ReturnsMatchStatusOnSuccessfulRoundTrip()
    {
        const string md = "Plain markdown content.";
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Utf8(md));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(md);

        var result = await CreateTool().InvokeAsync(Args(content: md, format: "markdown"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("status").GetString().Should().Be("MATCH");
        doc.GetProperty("charCount").GetInt32().Should().Be(md.Length);
    }

    [Fact]
    public async Task Markdown_MatchIgnoresTrailingNewlineDifference()
    {
        const string md = "some content";
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Utf8(md));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(md + "\n\n");

        var result = await CreateTool().InvokeAsync(Args(content: md, format: "markdown"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement
            .GetProperty("status").GetString().Should().Be("MATCH");
    }

    [Fact]
    public async Task Markdown_ReturnsMismatchWhenRoundTripFails()
    {
        const string md = "original";
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Utf8(md));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("completely different");

        var result = await CreateTool().InvokeAsync(Args(content: md, format: "markdown"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        JsonDocument.Parse(result.Text).RootElement
            .GetProperty("status").GetString().Should().Be("MISMATCH");
    }

    [Fact]
    public async Task Markdown_FormatIsCaseInsensitive()
    {
        const string md = "# Heading";
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Utf8(md));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(md);

        var result = await CreateTool().InvokeAsync(Args(content: md, format: "MARKDOWN"), CancellationToken.None);

        JsonDocument.Parse(result.Text).RootElement
            .GetProperty("status").GetString().Should().Be("MATCH");
    }

    // ── SHA-256 validation ───────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsSha256MismatchError_WhenHashDoesNotMatch()
    {
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Utf8("actual content"));

        // sha256 is computed from "different content", not "actual content"
        var args = Args(content: "different content");
        var result = await CreateTool().InvokeAsync(args, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("SHA-256 mismatch");
    }

    // ── slot read failure ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsError_WhenSlotReadFails()
    {
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .ThrowsAsync(new InvalidOperationException("blob not found"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("Failed to read upload slot");
    }

    // ── slot cleanup ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletesSlot_AfterSuccessfulWrite()
    {
        const string content = "<p>data</p>";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(content));
        _ado.GetFieldAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(content);

        await CreateTool().InvokeAsync(Args(content: content), CancellationToken.None);

        await _blobs.Received(1).DeleteSlotAsync("slot-1", Arg.Any<CancellationToken>());
    }
}
