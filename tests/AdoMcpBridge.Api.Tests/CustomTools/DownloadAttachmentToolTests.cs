using System.Security.Cryptography;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using AdoMcpBridge.Core.BlobStorage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public sealed class DownloadAttachmentToolTests
{
    private const string Guid1 = "11111111-1111-1111-1111-111111111111";

    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();
    private readonly IBlobSlotStore _blobs = Substitute.For<IBlobSlotStore>();

    private DownloadAttachmentTool CreateTool() =>
        new(_ado, _blobs, NullLogger<DownloadAttachmentTool>.Instance);

    private static JsonElement Args(
        string? id = null, string? url = null, string? fileName = null,
        string org = "myorg", string project = "myproject")
    {
        var props = new Dictionary<string, object?> { ["organization"] = org, ["project"] = project };
        if (id is not null) props["id"] = id;
        if (url is not null) props["url"] = url;
        if (fileName is not null) props["fileName"] = fileName;
        return JsonDocument.Parse(JsonSerializer.Serialize(props)).RootElement.Clone();
    }

    private void StubDownload(byte[] bytes, string contentType = "application/octet-stream") =>
        _ado.DownloadAttachmentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdoAttachmentContent(bytes, contentType));

    private void StubSlot(string slotId = "slot1", string url = "https://blob.example/slot1?sig=abc") =>
        _blobs.CreateDownloadSlotAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DownloadSlot(slotId, new Uri(url), DateTimeOffset.UtcNow.AddMinutes(15)));

    // ── happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_download_url_and_metadata_for_a_valid_id()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        _ado.DownloadAttachmentAsync("myorg", "myproject", Guid1, "pic.png", Arg.Any<CancellationToken>())
            .Returns(new AdoAttachmentContent(bytes, "image/png"));
        StubSlot(url: "https://blob.example/slot1?sig=abc");

        var result = await CreateTool().InvokeAsync(Args(id: Guid1, fileName: "pic.png"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Text).RootElement;
        json.GetProperty("downloadUrl").GetString().Should().Be("https://blob.example/slot1?sig=abc");
        json.GetProperty("fileName").GetString().Should().Be("pic.png");
        json.GetProperty("sizeBytes").GetInt32().Should().Be(5);
        json.GetProperty("contentType").GetString().Should().Be("image/png");
        json.GetProperty("sha256").GetString()
            .Should().Be(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        json.TryGetProperty("expiresAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Stages_the_bytes_with_their_content_type_into_a_download_slot()
    {
        var bytes = new byte[] { 9, 9, 9 };
        StubDownload(bytes, "application/pdf");
        StubSlot();

        await CreateTool().InvokeAsync(Args(id: Guid1), CancellationToken.None);

        await _blobs.Received(1).CreateDownloadSlotAsync(bytes, "application/pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolves_attachment_id_from_a_relation_url()
    {
        StubDownload([7]);
        StubSlot();

        var url = $"https://dev.azure.com/myorg/myproject/_apis/wit/attachments/{Guid1}";
        var result = await CreateTool().InvokeAsync(Args(url: url), CancellationToken.None);

        result.IsError.Should().BeFalse();
        await _ado.Received(1).DownloadAttachmentAsync(
            "myorg", "myproject", Guid1, Arg.Is<string?>(f => f == null), Arg.Any<CancellationToken>());
    }

    // ── argument validation ──────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_when_neither_id_nor_url_supplied()
    {
        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("exactly one");
    }

    [Fact]
    public async Task Rejects_when_both_id_and_url_supplied()
    {
        var result = await CreateTool().InvokeAsync(
            Args(id: Guid1, url: "https://dev.azure.com/o/p/_apis/wit/attachments/" + Guid1), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("exactly one");
    }

    [Fact]
    public async Task Rejects_an_id_that_is_not_a_guid()
    {
        var result = await CreateTool().InvokeAsync(Args(id: "not-a-guid"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("attachment GUID").And.Contain("id");
    }

    [Fact]
    public async Task Rejects_a_url_that_is_not_absolute()
    {
        var result = await CreateTool().InvokeAsync(Args(url: "not-a-url"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("attachment GUID").And.Contain("url");
    }

    [Fact]
    public async Task Rejects_a_url_whose_last_segment_is_not_a_guid()
    {
        var result = await CreateTool().InvokeAsync(
            Args(url: "https://dev.azure.com/o/p/_apis/wit/attachments/notaguid"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("attachment GUID");
    }

    // ── failure paths ────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_error_when_ado_download_fails()
    {
        _ado.DownloadAttachmentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("boom"));

        var result = await CreateTool().InvokeAsync(Args(id: Guid1), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO attachment download failed");
    }

    [Fact]
    public async Task Returns_error_when_creating_the_download_slot_fails()
    {
        StubDownload([1]);
        _blobs.CreateDownloadSlotAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("no storage"));

        var result = await CreateTool().InvokeAsync(Args(id: Guid1), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("Failed to create download slot");
    }

    // ── internal id extraction ───────────────────────────────────────────────

    [Theory]
    [InlineData("https://dev.azure.com/org/proj/_apis/wit/attachments/abc123", "abc123")]
    [InlineData("https://host/", null)]
    [InlineData("not-a-url", null)]
    public void ExtractAttachmentId_returns_the_last_segment_or_null(string url, string? expected)
    {
        DownloadAttachmentTool.ExtractAttachmentId(url).Should().Be(expected);
    }

    // ── tool metadata ────────────────────────────────────────────────────────

    [Fact]
    public void Advertises_itself_as_a_read_only_tool()
    {
        var tool = CreateTool();

        tool.Name.Should().Be("ado_bridge_download_attachment");
        JsonSerializer.Serialize(tool.Annotations).Should().Contain("readOnlyHint");
        tool.Description.Should().Contain("downloadUrl");
    }
}
