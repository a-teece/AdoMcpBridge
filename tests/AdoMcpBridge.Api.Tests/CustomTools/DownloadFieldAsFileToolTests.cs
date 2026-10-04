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

public sealed class DownloadFieldAsFileToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();
    private readonly IBlobSlotStore _blobs = Substitute.For<IBlobSlotStore>();

    private DownloadFieldAsFileTool CreateTool() =>
        new(_ado, _blobs, NullLogger<DownloadFieldAsFileTool>.Instance);

    private static JsonElement Args(
        string field = "System.Description", int id = 42,
        string org = "myorg", string project = "myproject") =>
        JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["organization"] = org,
            ["project"] = project,
            ["workItemId"] = id,
            ["fieldRefName"] = field,
        })).RootElement.Clone();

    private void StubField(string? value) =>
        _ado.GetFieldAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(value);

    private void StubSlot(string url = "https://blob.example/slot1?sig=abc") =>
        _blobs.CreateDownloadSlotAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DownloadSlot("slot1", new Uri(url), DateTimeOffset.UtcNow.AddMinutes(15)));

    // ── happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_download_url_and_metadata_without_the_field_content()
    {
        StubField("# Title\n\nSecret body text");
        StubSlot();

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Text.Should().NotContain("Secret body text");
        var json = JsonDocument.Parse(result.Text).RootElement;
        json.GetProperty("downloadUrl").GetString().Should().Be("https://blob.example/slot1?sig=abc");
        json.GetProperty("fileName").GetString().Should().Be("wi-42-System.Description.md");
        json.GetProperty("contentType").GetString().Should().Be("text/markdown; charset=utf-8");
        json.TryGetProperty("expiresAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Stages_the_unescaped_markdown_as_utf8_bytes()
    {
        const string stored = "a &lt; b &amp;&amp; c — ünïcode";
        StubField(stored);
        StubSlot();

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        var expected = Encoding.UTF8.GetBytes(AdoFieldEscaper.Unescape(stored));
        await _blobs.Received(1).CreateDownloadSlotAsync(
            Arg.Is<byte[]>(b => b.SequenceEqual(expected)),
            "text/markdown; charset=utf-8",
            Arg.Any<CancellationToken>());
        var json = JsonDocument.Parse(result.Text).RootElement;
        json.GetProperty("sizeBytes").GetInt32().Should().Be(expected.Length);
        json.GetProperty("sha256").GetString()
            .Should().Be(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant());
    }

    [Fact]
    public async Task Fetches_the_requested_field_from_the_requested_work_item()
    {
        StubField("x");
        StubSlot();

        await CreateTool().InvokeAsync(
            Args(field: "Custom.ImplementationPlan", id: 7, org: "o", project: "p"), CancellationToken.None);

        await _ado.Received(1).GetFieldAsync("o", "p", 7, "Custom.ImplementationPlan", Arg.Any<CancellationToken>());
    }

    // ── failure paths ────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_error_when_the_field_is_missing()
    {
        StubField(null);

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("System.Description").And.Contain("42");
        await _blobs.DidNotReceive().CreateDownloadSlotAsync(
            Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_error_surfacing_ado_status_and_message_on_non_success()
    {
        _ado.GetFieldAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Throws(new AdoRestException(403, "forbidden"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 403").And.Contain("forbidden");
    }

    [Fact]
    public async Task Returns_transport_error_when_the_ado_request_fails()
    {
        _ado.GetFieldAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed (transport)");
    }

    [Fact]
    public async Task Returns_error_when_creating_the_download_slot_fails()
    {
        StubField("x");
        _blobs.CreateDownloadSlotAsync(Arg.Any<byte[]>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("no storage"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("Failed to create download slot");
    }

    // ── tool metadata ────────────────────────────────────────────────────────

    [Fact]
    public void Advertises_itself_as_a_read_only_tool()
    {
        var tool = CreateTool();

        tool.Name.Should().Be("ado_bridge_download_field_as_file");
        JsonSerializer.Serialize(tool.Annotations).Should().Contain("readOnlyHint");
        tool.Description.Should().Contain("downloadUrl");
        JsonSerializer.Serialize(tool.InputSchema).Should().Contain("fieldRefName");
    }
}
