using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitListToolTests
{
    private const string Project = "Agile Playground";

    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();
    private readonly IWorkItemFieldTypeCache _cache = Substitute.For<IWorkItemFieldTypeCache>();

    public WitListToolTests()
    {
        _cache.GetLongTextFieldRefNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(["System.Description"]));
    }

    private WitListTool CreateTool() => new(_ado, _cache, NullLogger<WitListTool>.Instance);

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement WiqlResult(params int[] ids) => JsonSerializer.SerializeToElement(new
    {
        queryType = "flat",
        workItems = ids.Select(id => new { id, url = $"https://dev.azure.com/org/_apis/wit/workItems/{id}" }),
    });

    private static JsonElement WorkItem(int id, string title, string? description = null)
        => JsonSerializer.SerializeToElement(new
        {
            id,
            rev = 1,
            fields = new Dictionary<string, object?>
            {
                ["System.Title"] = title,
                ["System.Description"] = description,
            },
        });

    private void StubQuery(JsonElement result)
        => _ado.QueryByWiqlAsync(default!, default, default, default!, default, default, default)
            .ReturnsForAnyArgs(result);

    private void StubBatch(params JsonElement[] items)
        => _ado.GetWorkItemsBatchAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs(items.ToList());

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_NameReadOnlyHintAndSchema()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("ado_bridge_wit_list");
        JsonSerializer.SerializeToElement(tool.Annotations).GetProperty("readOnlyHint").GetBoolean()
            .Should().BeTrue();
        tool.Description.Should().Contain("@me").And.Contain("ado_bridge_wiql_query");

        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["organization", "project"]);
        var props = schema.GetProperty("properties");
        foreach (var p in new[]
                 {
                     "organization", "project", "types", "states", "assignedTo", "areaPath",
                     "iterationPath", "tags", "textContains", "top", "orderBy",
                 })
            props.TryGetProperty(p, out _).Should().BeTrue(p);
        props.GetProperty("orderBy").GetProperty("description").GetString()
            .Should().Contain("Microsoft.VSTS.Common.StackRank");
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task List_QueriesThenHydrates_ReturnsSlim()
    {
        StubQuery(WiqlResult(7, 3));
        // The batch API may return items in any order; the tool must keep WIQL order.
        StubBatch(WorkItem(3, "Second", "<p>long</p>"), WorkItem(7, "First"));

        var result = await CreateTool().InvokeAsync(Args(new
        {
            organization = "org",
            project = Project,
            types = new[] { "Bug" },
            assignedTo = "@me",
        }), default);

        result.IsError.Should().BeFalse();
        await _ado.Received(1).QueryByWiqlAsync(
            "org", Project, null,
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Agile Playground'" +
            " AND [System.WorkItemType] IN ('Bug') AND [System.AssignedTo] = @Me" +
            " ORDER BY [System.ChangedDate] DESC",
            51, null, Arg.Any<CancellationToken>());
        await _ado.Received(1).GetWorkItemsBatchAsync(
            "org", Project, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 7, 3 })),
            Arg.Any<CancellationToken>());

        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var items = root.GetProperty("workItems");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("id").GetInt32().Should().Be(7);
        items[1].GetProperty("id").GetInt32().Should().Be(3);
        // Slim projection: long-text field is stubbed, short fields pass through.
        items[1].GetProperty("fields").GetProperty("System.Description")
            .GetProperty("charCount").GetInt32().Should().Be("<p>long</p>".Length);
        items[1].GetProperty("fields").GetProperty("System.Title").GetString().Should().Be("Second");
    }

    [Fact]
    public async Task List_PassesAllFiltersAndOrderBy_ToWiql()
    {
        StubQuery(WiqlResult());

        await CreateTool().InvokeAsync(Args(new
        {
            organization = "org",
            project = Project,
            types = new[] { "Bug", "Task" },
            states = new[] { "Active" },
            assignedTo = "jo@example.com",
            areaPath = @"Agile Playground\A",
            iterationPath = @"Agile Playground\S1",
            tags = new[] { "t1" },
            textContains = "O'Brien",
            orderBy = "System.Id asc",
            top = 10,
        }), default);

        await _ado.Received(1).QueryByWiqlAsync(
            "org", Project, null,
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Agile Playground'" +
            " AND [System.WorkItemType] IN ('Bug', 'Task') AND [System.State] IN ('Active')" +
            " AND [System.AssignedTo] = 'jo@example.com'" +
            @" AND [System.AreaPath] UNDER 'Agile Playground\A'" +
            @" AND [System.IterationPath] UNDER 'Agile Playground\S1'" +
            " AND [System.Tags] CONTAINS 't1' AND [System.Title] CONTAINS 'O''Brien'" +
            " ORDER BY [System.Id] ASC",
            11, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_BlankOptionalStrings_AreIgnored()
    {
        StubQuery(WiqlResult());

        await CreateTool().InvokeAsync(Args(new
        {
            organization = "org",
            project = Project,
            assignedTo = " ",
            areaPath = "",
            iterationPath = (string?)null,
            textContains = "  ",
            orderBy = "",
        }), default);

        await _ado.Received(1).QueryByWiqlAsync(
            "org", Project, null,
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Agile Playground'" +
            " ORDER BY [System.ChangedDate] DESC",
            51, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_NoMatches_ReturnsEmpty()
    {
        StubQuery(WiqlResult());

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", project = Project }), default);

        result.IsError.Should().BeFalse();
        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(0);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        root.GetProperty("workItems").GetArrayLength().Should().Be(0);
        await _ado.DidNotReceiveWithAnyArgs().GetWorkItemsBatchAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task List_ResponseWithoutWorkItems_ReturnsEmpty()
    {
        StubQuery(Parse("{\"queryType\":\"flat\"}"));

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", project = Project }), default);

        result.IsError.Should().BeFalse();
        Parse(result.Text).GetProperty("workItems").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task List_MoreThanTop_SetsTruncated()
    {
        StubQuery(WiqlResult(1, 2, 3));
        StubBatch(WorkItem(1, "a"), WorkItem(2, "b"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = Project, top = 2 }), default);

        result.IsError.Should().BeFalse();
        await _ado.Received(1).QueryByWiqlAsync(
            Arg.Is("org"), Arg.Is(Project), Arg.Is<string?>(t => t == null), Arg.Any<string>(), 3, Arg.Is<bool?>(b => b == null),
            Arg.Any<CancellationToken>());
        await _ado.Received(1).GetWorkItemsBatchAsync(
            "org", Project, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 1, 2 })),
            Arg.Any<CancellationToken>());
        var root = Parse(result.Text);
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task List_ExactlyTop_IsNotTruncated()
    {
        StubQuery(WiqlResult(1, 2));
        StubBatch(WorkItem(1, "a"), WorkItem(2, "b"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = Project, top = 2 }), default);

        Parse(result.Text).GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task List_ItemMissingFromBatch_IsSkipped()
    {
        // An item deleted between the query and the hydrate must not break the listing.
        StubQuery(WiqlResult(1, 2));
        StubBatch(WorkItem(2, "b"));

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", project = Project }), default);

        var items = Parse(result.Text).GetProperty("workItems");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetInt32().Should().Be(2);
        Parse(result.Text).GetProperty("count").GetInt32().Should().Be(1);
    }

    // ── top boundaries ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 2)]
    [InlineData(200, 201)]
    [InlineData(201, 201)]
    [InlineData(100000, 201)]
    public async Task List_Top_IsCappedAt200(int top, int expectedQueryTop)
    {
        StubQuery(WiqlResult());

        await CreateTool().InvokeAsync(Args(new { organization = "org", project = Project, top }), default);

        await _ado.Received(1).QueryByWiqlAsync(
            Arg.Is("org"), Arg.Is(Project), Arg.Is<string?>(t => t == null), Arg.Any<string>(), expectedQueryTop,
            Arg.Is<bool?>(b => b == null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task List_TopBelowOne_Throws(int top)
    {
        var act = () => CreateTool().InvokeAsync(Args(new { organization = "org", project = Project, top }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("top");
    }

    // ── argument validation ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task List_MissingProject_Throws(string? project)
    {
        var act = () => CreateTool().InvokeAsync(Args(new { organization = "org", project }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("project");
        await _ado.DidNotReceiveWithAnyArgs()
            .QueryByWiqlAsync(default!, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task List_OrderByNotWhitelisted_ThrowsBeforeCallingAdo()
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = Project, orderBy = "System.Title" }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("orderBy");
        await _ado.DidNotReceiveWithAnyArgs()
            .QueryByWiqlAsync(default!, default, default, default!, default, default, default);
    }

    [Theory]
    [InlineData("{\"organization\":\"org\",\"project\":\"p\",\"types\":\"Bug\"}", "types")]
    [InlineData("{\"organization\":\"org\",\"project\":\"p\",\"states\":[1]}", "states")]
    [InlineData("{\"organization\":\"org\",\"project\":\"p\",\"tags\":[\"ok\",\" \"]}", "tags")]
    [InlineData("{\"organization\":\"org\",\"project\":\"p\",\"tags\":[null]}", "tags")]
    public async Task List_MalformedArrayArgument_Throws(string json, string name)
    {
        var act = () => CreateTool().InvokeAsync(Parse(json), default);

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain(name);
    }

    [Fact]
    public async Task List_NullArrayArgument_IsIgnored()
    {
        StubQuery(WiqlResult());

        await CreateTool().InvokeAsync(
            Parse("{\"organization\":\"org\",\"project\":\"p\",\"types\":null}"), default);

        await _ado.Received(1).QueryByWiqlAsync(
            "org", "p", null,
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'p' ORDER BY [System.ChangedDate] DESC",
            51, null, Arg.Any<CancellationToken>());
    }

    // ── ADO failures surface verbatim ────────────────────────────────────────

    [Fact]
    public async Task List_QueryAdoRestException_ReturnsIsError()
    {
        _ado.QueryByWiqlAsync(default!, default, default, default!, default, default, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(400, "TF51005: The query references a field that does not exist."));

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", project = Project }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 400").And.Contain("TF51005");
    }

    [Fact]
    public async Task List_HydrateAdoRestException_ReturnsIsError()
    {
        StubQuery(WiqlResult(1));
        _ado.GetWorkItemsBatchAsync(default!, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(503, "service unavailable"));

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", project = Project }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 503").And.Contain("service unavailable");
    }

    [Fact]
    public async Task List_HttpRequestException_ReturnsIsError()
    {
        _ado.QueryByWiqlAsync(default!, default, default, default!, default, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", project = Project }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed (transport)").And.Contain("connection reset");
    }
}
