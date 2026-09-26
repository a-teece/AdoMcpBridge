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

public class CreateWorkItemToolTests
{
    private readonly IBlobSlotStore _blobs = Substitute.For<IBlobSlotStore>();
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private CreateWorkItemTool CreateTool() =>
        new(_blobs, _ado, NullLogger<CreateWorkItemTool>.Instance);

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
    private static string Sha256Hex(string s) =>
        Convert.ToHexString(SHA256.HashData(Utf8(s))).ToLowerInvariant();

    // Builds the tool arguments. `fields` / `longTextFields` are lists of dictionaries so the
    // shape matches what an MCP client would send.
    private static JsonElement Args(
        string? organization = "myorg",
        string? project = "myproject",
        string? workItemType = "Bug",
        IEnumerable<object>? fields = null,
        IEnumerable<object>? longTextFields = null,
        object? fieldsRaw = null)
    {
        var props = new Dictionary<string, object?>();
        if (organization is not null) props["organization"] = organization;
        if (project is not null) props["project"] = project;
        if (workItemType is not null) props["workItemType"] = workItemType;
        if (fieldsRaw is not null) props["fields"] = fieldsRaw;
        else if (fields is not null) props["fields"] = fields;
        if (longTextFields is not null) props["longTextFields"] = longTextFields;

        return JsonDocument.Parse(JsonSerializer.Serialize(props)).RootElement.Clone();
    }

    private static object Scalar(string name, string value) =>
        new Dictionary<string, object?> { ["name"] = name, ["value"] = value };

    private static object LongText(string fieldRefName, string slotId, string sha256, string? format = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["fieldRefName"] = fieldRefName,
            ["slotId"] = slotId,
            ["sha256"] = sha256,
        };
        if (format is not null) d["format"] = format;
        return d;
    }

    private void SetupCreateReturns(int id = 100)
    {
        var el = JsonDocument.Parse($"{{\"id\":{id}}}").RootElement.Clone();
        _ado.CreateWorkItemAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>())
            .Returns(el);
    }

    // Serialises the ops passed to CreateWorkItemAsync so their JSON shape can be asserted,
    // exactly as the real REST client serialises them.
    private JsonElement CapturedOps()
    {
        var call = _ado.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IAdoRestClient.CreateWorkItemAsync));
        var ops = (IReadOnlyList<object>)call.GetArguments()[3]!;
        return JsonDocument.Parse(JsonSerializer.Serialize(ops)).RootElement.Clone();
    }

    // ── happy path: scalar fields only ─────────────────────────────────────────

    [Fact]
    public async Task ScalarOnly_CreatesAndReturnsId()
    {
        SetupCreateReturns(id: 777);

        var result = await CreateTool().InvokeAsync(
            Args(fields: [Scalar("System.Title", "My bug"), Scalar("System.State", "New")]),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("status").GetString().Should().Be("CREATED");
        doc.GetProperty("id").GetInt32().Should().Be(777);
        doc.GetProperty("scalarFieldCount").GetInt32().Should().Be(2);
        doc.GetProperty("longTextFieldCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ScalarOnly_BuildsFieldsOps()
    {
        SetupCreateReturns();

        await CreateTool().InvokeAsync(
            Args(fields: [Scalar("System.Title", "My bug"), Scalar("System.Tags", "a; b")]),
            CancellationToken.None);

        var ops = CapturedOps();
        ops.GetArrayLength().Should().Be(2);
        ops[0].GetProperty("op").GetString().Should().Be("add");
        ops[0].GetProperty("path").GetString().Should().Be("/fields/System.Title");
        ops[0].GetProperty("value").GetString().Should().Be("My bug");
        ops[1].GetProperty("path").GetString().Should().Be("/fields/System.Tags");
        ops[1].GetProperty("value").GetString().Should().Be("a; b");
    }

    [Fact]
    public async Task NoFieldsAtAll_CreatesWithEmptyOps()
    {
        SetupCreateReturns(id: 5);

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        CapturedOps().GetArrayLength().Should().Be(0);
        JsonDocument.Parse(result.Text).RootElement.GetProperty("id").GetInt32().Should().Be(5);
    }

    // ── happy path: markdown long-text ─────────────────────────────────────────

    [Fact]
    public async Task Markdown_EscapesValueAndAddsMultilineFormatOp()
    {
        const string md = "# Title\n\n`List<T>` and `x < y`";
        _blobs.ReadSlotAsync("slot-md", Arg.Any<CancellationToken>()).Returns(Utf8(md));
        SetupCreateReturns();

        var result = await CreateTool().InvokeAsync(
            Args(longTextFields: [LongText("System.Description", "slot-md", Sha256Hex(md), "markdown")]),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        var ops = CapturedOps();
        ops.GetArrayLength().Should().Be(2);
        ops[0].GetProperty("path").GetString().Should().Be("/fields/System.Description");
        ops[0].GetProperty("value").GetString().Should().Be(AdoFieldEscaper.Escape(md));
        ops[1].GetProperty("path").GetString().Should().Be("/multilineFieldsFormat/System.Description");
        ops[1].GetProperty("value").GetString().Should().Be("Markdown");

        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("longTextFieldCount").GetInt32().Should().Be(1);
        await _blobs.Received(1).DeleteSlotAsync("slot-md", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Markdown_IsTheDefaultWhenFormatOmitted()
    {
        const string md = "plain body";
        _blobs.ReadSlotAsync("slot-1", Arg.Any<CancellationToken>()).Returns(Utf8(md));
        SetupCreateReturns();

        await CreateTool().InvokeAsync(
            Args(longTextFields: [LongText("System.Description", "slot-1", Sha256Hex(md))]),
            CancellationToken.None);

        var ops = CapturedOps();
        ops.GetArrayLength().Should().Be(2);
        ops[0].GetProperty("value").GetString().Should().Be(AdoFieldEscaper.Escape(md));
        ops[1].GetProperty("path").GetString().Should().Be("/multilineFieldsFormat/System.Description");
    }

    // ── happy path: html long-text ─────────────────────────────────────────────

    [Fact]
    public async Task Html_SendsContentAsIsWithNoMultilineFormatOp()
    {
        const string html = "<h1>T</h1><p>x &amp; y</p>";
        _blobs.ReadSlotAsync("slot-h", Arg.Any<CancellationToken>()).Returns(Utf8(html));
        SetupCreateReturns();

        await CreateTool().InvokeAsync(
            Args(longTextFields: [LongText("System.Description", "slot-h", Sha256Hex(html), "html")]),
            CancellationToken.None);

        var ops = CapturedOps();
        ops.GetArrayLength().Should().Be(1);
        ops[0].GetProperty("path").GetString().Should().Be("/fields/System.Description");
        ops[0].GetProperty("value").GetString().Should().Be(html);   // not escaped
    }

    [Fact]
    public async Task Format_IsCaseInsensitive()
    {
        const string html = "<p>hi</p>";
        _blobs.ReadSlotAsync("slot-h", Arg.Any<CancellationToken>()).Returns(Utf8(html));
        SetupCreateReturns();

        await CreateTool().InvokeAsync(
            Args(longTextFields: [LongText("System.Description", "slot-h", Sha256Hex(html), "HTML")]),
            CancellationToken.None);

        CapturedOps().GetArrayLength().Should().Be(1);   // no format op → HTML path taken
    }

    [Fact]
    public async Task MixedScalarAndLongText_AreAllCreatedInOnePost()
    {
        const string md = "body";
        _blobs.ReadSlotAsync("s1", Arg.Any<CancellationToken>()).Returns(Utf8(md));
        SetupCreateReturns(id: 9);

        var result = await CreateTool().InvokeAsync(
            Args(
                fields: [Scalar("System.Title", "T")],
                longTextFields: [LongText("System.Description", "s1", Sha256Hex(md))]),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("scalarFieldCount").GetInt32().Should().Be(1);
        doc.GetProperty("longTextFieldCount").GetInt32().Should().Be(1);
        // 1 scalar + 1 field + 1 multilineFieldsFormat
        CapturedOps().GetArrayLength().Should().Be(3);
        await _ado.Received(1).CreateWorkItemAsync(
            "myorg", "myproject", "Bug", Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>());
    }

    // ── failures BEFORE the create (no partial work item) ──────────────────────

    [Fact]
    public async Task Sha256Mismatch_ReturnsError_AndDoesNotCreate()
    {
        _blobs.ReadSlotAsync("s1", Arg.Any<CancellationToken>()).Returns(Utf8("actual content"));

        var result = await CreateTool().InvokeAsync(
            Args(longTextFields: [LongText("System.Description", "s1", Sha256Hex("different"))]),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("SHA-256 mismatch");
        await _ado.DidNotReceive().CreateWorkItemAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>());
        await _blobs.DidNotReceive().DeleteSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SlotReadFailure_ReturnsError_AndDoesNotCreate()
    {
        _blobs.ReadSlotAsync("s1", Arg.Any<CancellationToken>())
              .ThrowsAsync(new InvalidOperationException("blob not found"));

        var result = await CreateTool().InvokeAsync(
            Args(longTextFields: [LongText("System.Description", "s1", Sha256Hex("x"))]),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("Failed to read upload slot");
        await _ado.DidNotReceive().CreateWorkItemAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidFormat_ReturnsError_AndDoesNotCreate()
    {
        var result = await CreateTool().InvokeAsync(
            Args(longTextFields: [LongText("System.Description", "s1", Sha256Hex("x"), "htm")]),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("must be 'markdown' or 'html'");
        result.Text.Should().Contain("htm");
        await _blobs.DidNotReceive().ReadSlotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _ado.DidNotReceive().CreateWorkItemAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>());
    }

    // ── errors from the create call ────────────────────────────────────────────

    [Fact]
    public async Task AdoRestException_SurfacesStatusAndMessage()
    {
        _ado.CreateWorkItemAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AdoRestException(400, "The field 'System.Title' is required."));

        var result = await CreateTool().InvokeAsync(
            Args(fields: [Scalar("System.State", "New")]), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 400");
        result.Text.Should().Contain("System.Title");
    }

    [Fact]
    public async Task HttpRequestException_ReturnsTransportError()
    {
        _ado.CreateWorkItemAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("transport");
    }

    // ── created element without an id ───────────────────────────────────────────

    [Fact]
    public async Task CreatedWithoutNumericId_ReturnsNullId()
    {
        _ado.CreateWorkItemAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>())
            .Returns(JsonDocument.Parse("{\"fields\":{}}").RootElement.Clone());

        var result = await CreateTool().InvokeAsync(Args(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("status").GetString().Should().Be("CREATED");
        doc.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── slot cleanup is best-effort ────────────────────────────────────────────

    [Fact]
    public async Task DeleteSlotFailure_IsNonFatal()
    {
        const string md = "body";
        _blobs.ReadSlotAsync("s1", Arg.Any<CancellationToken>()).Returns(Utf8(md));
        _blobs.DeleteSlotAsync("s1", Arg.Any<CancellationToken>())
              .ThrowsAsync(new InvalidOperationException("delete failed"));
        SetupCreateReturns(id: 3);

        var result = await CreateTool().InvokeAsync(
            Args(longTextFields: [LongText("System.Description", "s1", Sha256Hex(md))]),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        JsonDocument.Parse(result.Text).RootElement.GetProperty("id").GetInt32().Should().Be(3);
    }

    // ── missing / malformed arguments (-32602) ─────────────────────────────────

    [Fact]
    public async Task MissingOrganization_ThrowsCallerArgumentException()
    {
        var act = () => CreateTool().InvokeAsync(Args(organization: null), CancellationToken.None);

        await act.Should().ThrowAsync<CallerArgumentException>();
    }

    [Fact]
    public async Task MissingWorkItemType_ThrowsCallerArgumentException()
    {
        var act = () => CreateTool().InvokeAsync(Args(workItemType: null), CancellationToken.None);

        await act.Should().ThrowAsync<CallerArgumentException>();
    }

    [Fact]
    public async Task ScalarEntryMissingValue_ThrowsCallerArgumentException()
    {
        var malformed = new Dictionary<string, object?> { ["name"] = "System.Title" };

        var act = () => CreateTool().InvokeAsync(Args(fields: [malformed]), CancellationToken.None);

        await act.Should().ThrowAsync<CallerArgumentException>();
    }

    [Fact]
    public async Task LongTextEntryMissingSlotId_ThrowsCallerArgumentException()
    {
        var malformed = new Dictionary<string, object?>
        {
            ["fieldRefName"] = "System.Description",
            ["sha256"] = "abc",
        };

        var act = () => CreateTool().InvokeAsync(
            Args(longTextFields: [malformed]), CancellationToken.None);

        await act.Should().ThrowAsync<CallerArgumentException>();
    }

    [Fact]
    public async Task FieldsNotAnArray_ThrowsCallerArgumentException()
    {
        var act = () => CreateTool().InvokeAsync(
            Args(fieldsRaw: "not-an-array"), CancellationToken.None);

        await act.Should().ThrowAsync<CallerArgumentException>();
    }

    [Fact]
    public async Task FieldsExplicitlyJsonNull_IsTreatedAsEmpty()
    {
        SetupCreateReturns(id: 8);
        var args = JsonDocument.Parse(
            "{\"organization\":\"o\",\"project\":\"p\",\"workItemType\":\"Bug\",\"fields\":null,\"longTextFields\":null}")
            .RootElement.Clone();

        var result = await CreateTool().InvokeAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        CapturedOps().GetArrayLength().Should().Be(0);
    }
}
