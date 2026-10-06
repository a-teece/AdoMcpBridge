using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitSearchToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private WitSearchTool CreateTool() => new(_ado, NullLogger<WitSearchTool>.Instance);

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // Shape captured live from almsearch.dev.azure.com (2026-10-06); values are synthetic.
    // system.id arrives as a string; system.assignedto is "Name <email>" or empty when unassigned.
    private const string TwoHits =
        "{\"count\":2,\"results\":[" +
        "{\"project\":{\"name\":\"Andrews Weather PoC\",\"id\":\"p-1\"}," +
        "\"fields\":{\"system.id\":\"101\",\"system.workitemtype\":\"Bug\",\"system.title\":\"Rain gauge drifts\"," +
        "\"system.assignedto\":\"Jo Bloggs <jo.bloggs@example.com>\",\"system.state\":\"Active\"," +
        "\"system.tags\":\"\",\"system.rev\":\"3\",\"system.description\":\"<div>long</div>\"}," +
        "\"hits\":[{\"fieldReferenceName\":\"system.title\",\"highlights\":[\"<highlighthit>Rain</highlighthit>\"]}]," +
        "\"url\":\"https://dev.azure.com/org/_apis/wit/workItems/101\"}," +
        "{\"project\":{\"name\":\"Andrews Weather PoC\",\"id\":\"p-1\"}," +
        "\"fields\":{\"system.id\":\"102\",\"system.workitemtype\":\"Task\",\"system.title\":\"Rain alerts\"," +
        "\"system.assignedto\":\"\",\"system.state\":\"New\"}," +
        "\"hits\":[],\"url\":\"https://dev.azure.com/org/_apis/wit/workItems/102\"}" +
        "],\"infoCode\":0,\"facets\":{}}";

    private const string NoHits = "{\"count\":0,\"results\":[],\"infoCode\":0,\"facets\":{}}";

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_NameReadOnlyHintAndSchema()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("ado_bridge_wit_search");
        JsonSerializer.SerializeToElement(tool.Annotations).GetProperty("readOnlyHint").GetBoolean()
            .Should().BeTrue();
        tool.Description.Should().Contain("ado_bridge_wit_get_batch");

        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["organization", "searchText"]);
        var props = schema.GetProperty("properties");
        foreach (var p in new[] { "organization", "project", "searchText", "top", "skip" })
            props.TryGetProperty(p, out _).Should().BeTrue(p);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ReturnsSlimResults()
    {
        _ado.SearchWorkItemsAsync("org", "Andrews Weather PoC", "rain", 25, 0, Arg.Any<CancellationToken>())
            .Returns(Parse(TwoHits));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "Andrews Weather PoC", searchText = "rain" }), default);

        result.IsError.Should().BeFalse();
        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("top").GetInt32().Should().Be(25);
        root.GetProperty("skip").GetInt32().Should().Be(0);

        var results = root.GetProperty("results");
        results.GetArrayLength().Should().Be(2);

        var first = results[0];
        first.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo(["id", "type", "title", "state", "assignedTo", "project"]);
        first.GetProperty("id").GetInt32().Should().Be(101);
        first.GetProperty("type").GetString().Should().Be("Bug");
        first.GetProperty("title").GetString().Should().Be("Rain gauge drifts");
        first.GetProperty("state").GetString().Should().Be("Active");
        first.GetProperty("assignedTo").GetString().Should().Be("Jo Bloggs <jo.bloggs@example.com>");
        first.GetProperty("project").GetString().Should().Be("Andrews Weather PoC");

        // Unassigned arrives as "" — project it as null rather than an empty name.
        results[1].GetProperty("id").GetInt32().Should().Be(102);
        results[1].GetProperty("assignedTo").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Search_PassesExplicitTopAndSkip_AndOmitsProjectWhenAbsent()
    {
        _ado.SearchWorkItemsAsync(default!, default, default!, default, default, default)
            .ReturnsForAnyArgs(Parse(NoHits));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", searchText = "rain", top = 10, skip = 20 }), default);

        result.IsError.Should().BeFalse();
        await _ado.Received(1).SearchWorkItemsAsync("org", null, "rain", 10, 20, Arg.Any<CancellationToken>());
        var root = Parse(result.Text);
        root.GetProperty("top").GetInt32().Should().Be(10);
        root.GetProperty("skip").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task Search_ToleratesMissingFieldsAndProject()
    {
        _ado.SearchWorkItemsAsync(default!, default, default!, default, default, default)
            .ReturnsForAnyArgs(Parse(
                "{\"results\":[" +
                "{\"fields\":{\"system.id\":\"7\"}}," +                                   // only an id
                "{}," +                                                                   // no fields, no project
                "{\"fields\":{\"system.id\":\"abc\",\"system.title\":5},\"project\":\"x\"}," + // malformed values
                "{\"project\":{\"id\":\"p-1\"}}" +                                        // project without name
                "]}"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", searchText = "x" }), default);

        result.IsError.Should().BeFalse();
        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(0);
        var items = root.GetProperty("results");
        items.GetArrayLength().Should().Be(4);
        items[0].GetProperty("id").GetInt32().Should().Be(7);
        for (var i = 0; i < 4; i++)
        {
            if (i > 0) items[i].GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
            foreach (var p in new[] { "type", "title", "state", "assignedTo", "project" })
                items[i].GetProperty(p).ValueKind.Should().Be(JsonValueKind.Null, $"{i}.{p}");
        }
    }

    [Fact]
    public async Task Search_ResponseWithoutResults_ReturnsEmptyResults()
    {
        _ado.SearchWorkItemsAsync(default!, default, default!, default, default, default)
            .ReturnsForAnyArgs(Parse("{}"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", searchText = "x" }), default);

        result.IsError.Should().BeFalse();
        Parse(result.Text).GetProperty("results").GetArrayLength().Should().Be(0);
    }

    // ── Review Focus 3: zero hits / skip past end / top above cap ───────────

    [Fact]
    public async Task Search_ZeroHits_ReturnsEmptyResults_NotError()
    {
        _ado.SearchWorkItemsAsync(default!, default, default!, default, default, default)
            .ReturnsForAnyArgs(Parse(NoHits));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", searchText = "zzqxjkvnotaword" }), default);

        result.IsError.Should().BeFalse();
        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(0);
        root.GetProperty("results").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Search_SkipPastEnd_ReturnsEmptyResults()
    {
        // Live: count=61, $skip=100 → 200 with the true count and an empty results[].
        _ado.SearchWorkItemsAsync("org", null, "rain", 25, 100, Arg.Any<CancellationToken>())
            .Returns(Parse("{\"count\":61,\"results\":[],\"infoCode\":0,\"facets\":{}}"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", searchText = "rain", skip = 100 }), default);

        result.IsError.Should().BeFalse();
        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(61);
        root.GetProperty("skip").GetInt32().Should().Be(100);
        root.GetProperty("results").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Search_TopAboveMax_IsClampedTo100()
    {
        _ado.SearchWorkItemsAsync(default!, default, default!, default, default, default)
            .ReturnsForAnyArgs(Parse(NoHits));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", searchText = "rain", top = 5000 }), default);

        result.IsError.Should().BeFalse();
        await _ado.Received(1).SearchWorkItemsAsync("org", null, "rain", 100, 0, Arg.Any<CancellationToken>());
        Parse(result.Text).GetProperty("top").GetInt32().Should().Be(100);
    }

    // ── argument validation ──────────────────────────────────────────────────

    [Fact]
    public async Task Search_MissingSearchText_ThrowsCallerArgumentException()
    {
        var act = () => CreateTool().InvokeAsync(Args(new { organization = "org" }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("searchText");
        await _ado.DidNotReceiveWithAnyArgs()
            .SearchWorkItemsAsync(default!, default, default!, default, default, default);
    }

    [Theory]
    [InlineData(0, 0, "top")]
    [InlineData(-1, 0, "top")]
    [InlineData(25, -1, "skip")]
    [InlineData(25, 1001, "skip")]
    public async Task Search_OutOfRangeTopOrSkip_ThrowsCallerArgumentException(int top, int skip, string name)
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", searchText = "rain", top, skip }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain(name);
    }

    // ── ADO failures surface verbatim ────────────────────────────────────────

    [Fact]
    public async Task Search_AdoRestException_ReturnsIsError()
    {
        _ado.SearchWorkItemsAsync(default!, default, default!, default, default, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(404,
                "TF200016: The following project does not exist: nope."));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "nope", searchText = "rain" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("404").And.Contain("TF200016");
    }

    [Fact]
    public async Task Search_HttpRequestException_ReturnsIsError()
    {
        _ado.SearchWorkItemsAsync(default!, default, default!, default, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", searchText = "rain" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("connection reset");
    }
}
