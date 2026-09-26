using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public sealed class ReadFileToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private ReadFileTool CreateTool() => new(_ado, NullLogger<ReadFileTool>.Instance);

    private static JsonElement Args(
        string? organization = "myorg", string? project = "myproject",
        string? repositoryId = "my-repo", string? path = "/src/Foo.cs",
        string? version = null, string? versionType = null)
    {
        var props = new Dictionary<string, object?>();
        if (organization is not null) props["organization"] = organization;
        if (project is not null) props["project"] = project;
        if (repositoryId is not null) props["repositoryId"] = repositoryId;
        if (path is not null) props["path"] = path;
        if (version is not null) props["version"] = version;
        if (versionType is not null) props["versionType"] = versionType;
        return JsonDocument.Parse(JsonSerializer.Serialize(props)).RootElement.Clone();
    }

    private void StubContent(byte[] bytes, string contentType = "text/plain") =>
        _ado.GetRepoItemContentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AdoRepoItemContent(bytes, contentType));

    // ── happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_text_content_inline_for_a_text_file()
    {
        const string body = "public class Foo { }\n";
        StubContent(Encoding.UTF8.GetBytes(body));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Text.Should().Be(body);
    }

    [Fact]
    public async Task Passes_version_and_version_type_through_to_the_client()
    {
        StubContent(Encoding.UTF8.GetBytes("x"));

        await CreateTool().InvokeAsync(
            Args(version: "main", versionType: "Branch"), CancellationToken.None);

        await _ado.Received(1).GetRepoItemContentAsync(
            "myorg", "myproject", "my-repo", "/src/Foo.cs", "main", "Branch", Arg.Any<CancellationToken>());
    }

    // ── size cap ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_a_file_over_the_inline_size_cap()
    {
        var big = new byte[1_048_577];
        // Fill with a printable ASCII byte so only the size check trips (not binary detection).
        Array.Fill(big, (byte)'a');
        StubContent(big);

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("1048577 bytes")
            .And.Contain("1048576-byte inline read limit")
            .And.Contain("repo_file get_content");
    }

    // ── binary detection ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_content_containing_a_nul_byte_as_binary()
    {
        StubContent([0x41, 0x00, 0x42], "application/octet-stream");

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("appears to be binary")
            .And.Contain("application/octet-stream")
            .And.Contain("repo_file get_content");
    }

    [Fact]
    public async Task Rejects_content_that_is_not_valid_utf8_as_binary()
    {
        // 0xFF is never valid in UTF-8, and there is no NUL byte, so strict decoding must trip.
        StubContent([0xFF, 0xFE, 0xFD], "image/png");

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("appears to be binary");
    }

    // ── failure paths ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_file_not_found_when_client_returns_null()
    {
        _ado.GetRepoItemContentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((AdoRepoItemContent?)null);

        var result = await CreateTool().InvokeAsync(Args(path: "/nope.cs"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Be("File not found: /nope.cs");
    }

    [Fact]
    public async Task Returns_error_surfacing_ado_status_and_message_on_rest_exception()
    {
        _ado.GetRepoItemContentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new AdoRestException(403, "access denied"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 403").And.Contain("access denied");
    }

    [Fact]
    public async Task Returns_transport_error_when_the_request_fails()
    {
        _ado.GetRepoItemContentAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed (transport)");
    }

    // ── argument validation ────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_a_missing_path_argument()
    {
        var act = () => CreateTool().InvokeAsync(Args(path: null), CancellationToken.None);

        await act.Should().ThrowAsync<CallerArgumentException>()
            .WithMessage("'path' is required and must be a non-empty string.");
        await _ado.DidNotReceiveWithAnyArgs().GetRepoItemContentAsync(
            default!, default!, default!, default!, default, default, default);
    }

    // ── tool metadata ────────────────────────────────────────────────────────────

    [Fact]
    public void Advertises_itself_as_a_read_only_tool()
    {
        var tool = CreateTool();

        tool.Name.Should().Be("ado_bridge_read_file");
        JsonSerializer.Serialize(tool.Annotations).Should().Contain("readOnlyHint");
        tool.Description.Should().Contain("INLINE");
    }
}
