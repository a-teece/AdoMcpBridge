using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitListQueriesToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private WitListQueriesTool CreateTool() => new(_ado, NullLogger<WitListQueriesTool>.Instance);

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // Shape captured live from GET _apis/wit/queries?$depth=2&$expand=none (2026-10-06): root
    // folders carry isFolder/hasChildren; nested items arrive under "children"; query items omit
    // isFolder. Identity/_links/url noise trimmed; ids and names below the roots are synthetic.
    private const string Tree =
        "{\"count\":2,\"value\":[" +
        "{\"id\":\"8c070a12-8457-43f9-96d3-b281e23fabf5\",\"name\":\"Shared Queries\",\"path\":\"Shared Queries\"," +
        "\"createdDate\":\"2023-03-08T13:08:31.473Z\",\"isFolder\":true,\"hasChildren\":true,\"isPublic\":true," +
        "\"children\":[" +
        "{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Triage\",\"path\":\"Shared Queries/Triage\"," +
        "\"isFolder\":true,\"hasChildren\":true,\"isPublic\":true,\"children\":[" +
        "{\"id\":\"22222222-2222-2222-2222-222222222222\",\"name\":\"New bugs\"," +
        "\"path\":\"Shared Queries/Triage/New bugs\",\"isPublic\":true}]}," +
        "{\"id\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"Active work\"," +
        "\"path\":\"Shared Queries/Active work\",\"isFolder\":false,\"isPublic\":true}]," +
        "\"url\":\"https://dev.azure.com/Enate/p/_apis/wit/queries/8c070a12\"}," +
        "{\"id\":\"0d6cf79d-59ae-4d2d-a89d-aa7ccf324ccb\",\"name\":\"My Queries\",\"path\":\"My Queries\"," +
        "\"isFolder\":true,\"hasChildren\":false,\"isPublic\":false}" +
        "]}";

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_NameReadOnlyHintAndSchema()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("ado_bridge_wit_list_queries");
        JsonSerializer.SerializeToElement(tool.Annotations).GetProperty("readOnlyHint").GetBoolean()
            .Should().BeTrue();
        tool.Description.Should().Contain("ado_bridge_wit_run_query");

        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["organization", "project"]);
        var props = schema.GetProperty("properties");
        foreach (var p in new[] { "organization", "project", "depth" })
            props.TryGetProperty(p, out _).Should().BeTrue(p);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListQueries_FlattensFolders()
    {
        _ado.ListQueriesAsync("org", "Agile Playground", 2, Arg.Any<CancellationToken>()).Returns(Parse(Tree));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "Agile Playground" }), default);

        result.IsError.Should().BeFalse();
        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(5);
        var items = root.GetProperty("queries").EnumerateArray().ToList();

        // Pre-order: each folder precedes its children; every item is {id,name,path,isFolder}.
        items.Select(i => i.GetProperty("path").GetString()).Should().Equal(
            "Shared Queries",
            "Shared Queries/Triage",
            "Shared Queries/Triage/New bugs",
            "Shared Queries/Active work",
            "My Queries");
        items.Select(i => i.GetProperty("isFolder").GetBoolean()).Should().Equal(true, true, false, false, true);
        foreach (var item in items)
            item.EnumerateObject().Select(p => p.Name).Should().Equal("id", "name", "path", "isFolder");

        items[2].GetProperty("id").GetString().Should().Be("22222222-2222-2222-2222-222222222222");
        items[2].GetProperty("name").GetString().Should().Be("New bugs");
    }

    [Fact]
    public async Task ListQueries_ResponseWithoutValue_ReturnsEmptyList()
    {
        _ado.ListQueriesAsync(default!, default!, default, default).ReturnsForAnyArgs(Parse("{}"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "p" }), default);

        result.IsError.Should().BeFalse();
        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(0);
        root.GetProperty("queries").GetArrayLength().Should().Be(0);
    }

    // ── depth bounds (1–2; ADO itself rejects $depth > 2) ────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ListQueries_DepthAtEitherBound_IsForwarded(int depth)
    {
        _ado.ListQueriesAsync(default!, default!, default, default).ReturnsForAnyArgs(Parse("{\"value\":[]}"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "p", depth }), default);

        result.IsError.Should().BeFalse();
        await _ado.Received(1).ListQueriesAsync("org", "p", depth, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task ListQueries_DepthOutsideBounds_ThrowsCallerArgumentException(int depth)
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "p", depth }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("depth");
        await _ado.DidNotReceiveWithAnyArgs().ListQueriesAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task ListQueries_MissingProject_ThrowsCallerArgumentException()
    {
        var act = () => CreateTool().InvokeAsync(Args(new { organization = "org" }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("project");
    }

    // ── ADO failures surface verbatim ────────────────────────────────────────

    [Fact]
    public async Task ListQueries_AdoRestException_ReturnsIsError()
    {
        _ado.ListQueriesAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(404,
                "TF200016: The following project does not exist: nope."));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "nope" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("404").And.Contain("TF200016");
    }

    [Fact]
    public async Task ListQueries_HttpRequestException_ReturnsIsError()
    {
        _ado.ListQueriesAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "p" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("connection reset");
    }
}
