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

public sealed class UploadAttachmentFromSlotToolTests
{
    private readonly IBlobSlotStore _blobs = Substitute.For<IBlobSlotStore>();
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private UploadAttachmentFromSlotTool CreateTool() =>
        new(_blobs, _ado, NullLogger<UploadAttachmentFromSlotTool>.Instance);

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static JsonElement Args(
        string slotId = "slot-1", string org = "myorg", string project = "myproject",
        string fileName = "file.bin", string? sha256 = null, int? workItemId = null,
        string? comment = null, string content = "hello")
    {
        var hash = sha256 ?? Convert.ToHexString(SHA256.HashData(Utf8(content))).ToLowerInvariant();
        var props = new Dictionary<string, object?>
        {
            ["slotId"] = slotId,
            ["organization"] = org,
            ["project"] = project,
            ["fileName"] = fileName,
            ["sha256"] = hash,
        };
        if (workItemId is not null) props["workItemId"] = workItemId;
        if (comment is not null) props["comment"] = comment;
        return JsonDocument.Parse(JsonSerializer.Serialize(props)).RootElement.Clone();
    }

    private void StubSlotBytes(byte[] bytes) =>
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(bytes);

    private void StubUpload(string id = "att-id", string url = "https://dev.azure.com/o/_apis/wit/attachments/att-id") =>
        _ado.CreateAttachmentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdoAttachmentRef(id, url));

    // ── happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Uploads_and_links_to_a_work_item()
    {
        var bytes = Utf8("hello");
        StubSlotBytes(bytes);
        _ado.CreateAttachmentAsync("myorg", "myproject", "file.bin", Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new AdoAttachmentRef("att-id", "https://x/_apis/wit/attachments/att-id"));

        var result = await CreateTool().InvokeAsync(Args(workItemId: 42, comment: "look"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Text).RootElement;
        json.GetProperty("status").GetString().Should().Be("LINKED");
        json.GetProperty("attachmentId").GetString().Should().Be("att-id");
        json.GetProperty("attachmentUrl").GetString().Should().Be("https://x/_apis/wit/attachments/att-id");
        json.GetProperty("fileName").GetString().Should().Be("file.bin");
        json.GetProperty("sizeBytes").GetInt32().Should().Be(bytes.Length);
        json.GetProperty("linkedToWorkItem").GetInt32().Should().Be(42);

        await _ado.Received(1).AddWorkItemAttachmentAsync(
            "myorg", "myproject", 42, "https://x/_apis/wit/attachments/att-id", "look", Arg.Any<CancellationToken>());
        await _blobs.Received(1).DeleteSlotAsync("slot-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uploads_without_linking_when_no_work_item_is_given()
    {
        StubSlotBytes(Utf8("hello"));
        StubUpload();

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var json = JsonDocument.Parse(result.Text).RootElement;
        json.GetProperty("status").GetString().Should().Be("UPLOADED");
        json.GetProperty("linkedToWorkItem").ValueKind.Should().Be(JsonValueKind.Null);

        await _ado.DidNotReceive().AddWorkItemAttachmentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── failure paths ────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_error_when_slot_read_fails()
    {
        _blobs.ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("gone"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("Failed to read upload slot");
    }

    [Fact]
    public async Task Returns_error_on_sha_mismatch_without_uploading()
    {
        StubSlotBytes(Utf8("hello"));

        var result = await CreateTool().InvokeAsync(Args(sha256: "deadbeef"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("SHA-256 mismatch");
        await _ado.DidNotReceive().CreateAttachmentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_error_when_attachment_upload_fails()
    {
        StubSlotBytes(Utf8("hello"));
        _ado.CreateAttachmentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("boom"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO attachment upload failed");
    }

    [Fact]
    public async Task Reports_upload_link_failed_when_only_the_link_fails()
    {
        StubSlotBytes(Utf8("hello"));
        _ado.CreateAttachmentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdoAttachmentRef("att-id", "att-url"));
        _ado.AddWorkItemAttachmentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("nope"));

        var result = await CreateTool().InvokeAsync(Args(workItemId: 42), CancellationToken.None);

        result.IsError.Should().BeTrue();
        var json = JsonDocument.Parse(result.Text).RootElement;
        json.GetProperty("status").GetString().Should().Be("UPLOADED_LINK_FAILED");
        json.GetProperty("attachmentId").GetString().Should().Be("att-id");
        json.GetProperty("attachmentUrl").GetString().Should().Be("att-url");
    }

    [Fact]
    public async Task Succeeds_even_when_slot_delete_fails()
    {
        StubSlotBytes(Utf8("hello"));
        StubUpload();
        _blobs.DeleteSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement.GetProperty("status").GetString().Should().Be("UPLOADED");
    }

    // ── tool metadata ────────────────────────────────────────────────────────

    [Fact]
    public void Advertises_itself_as_a_write_tool_pointing_at_the_upload_slot()
    {
        var tool = CreateTool();

        tool.Name.Should().Be("ado_bridge_upload_attachment_from_slot");
        JsonSerializer.Serialize(tool.Annotations).Should().Contain("readOnlyHint");
        tool.Description.Should().Contain("ado_bridge_create_upload_slot");
    }
}
