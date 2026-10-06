using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitHistoryToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private WitHistoryTool CreateTool() => new(_ado, NullLogger<WitHistoryTool>.Instance);

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // Shape captured live (2026-10-06) from GET _apis/wit/workItems/{id}/updates; values synthetic.
    private static string Identity(string name) =>
        $"{{\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"{name} <{name}@example.test>\"," +
        $"\"displayName\":\"{name}\",\"url\":\"https://x/identity\"," +
        "\"_links\":{\"avatar\":{\"href\":\"https://x/avatar\"}}," +
        $"\"uniqueName\":\"{name}@example.test\",\"imageUrl\":\"https://x/avatar\",\"descriptor\":\"aad.x\"}}";

    private static string Update(int id, int rev, string? fields = null, string? relations = null)
    {
        var json = $"{{\"id\":{id},\"workItemId\":42,\"rev\":{rev},\"revisedBy\":{Identity("User A")}," +
            $"\"revisedDate\":\"2026-01-{id % 28 + 1:00}T10:00:00Z\"";
        if (fields is not null) json += $",\"fields\":{fields}";
        if (relations is not null) json += $",\"relations\":{relations}";
        return json + ",\"url\":\"https://x/updates/" + id + "\"}";
    }

    private static string Page(IEnumerable<string> updates)
    {
        var list = updates.ToList();
        return $"{{\"count\":{list.Count},\"value\":[{string.Join(",", list)}]}}";
    }

    // n simple updates (ids/revs 1..n, oldest first as ADO returns them).
    private static string Simple(int from, int to)
        => Page(Enumerable.Range(from, to - from + 1).Select(i => Update(i, i,
            $"{{\"System.Rev\":{{\"oldValue\":{i - 1},\"newValue\":{i}}}}}")));

    private void ReturnsPage(int skip, string json)
        => _ado.GetWorkItemUpdatesAsync("org", "proj", 42, WitHistoryTool.AdoPageSize, skip, Arg.Any<CancellationToken>())
            .Returns(Parse(json));

    private async Task<JsonElement> InvokeOk(object args)
    {
        var result = await CreateTool().InvokeAsync(Args(args), default);
        result.IsError.Should().BeFalse(result.Text);
        return Parse(result.Text);
    }

    private static int[] Revs(JsonElement root)
        => root.EnumerateArray().Select(e => e.GetProperty("rev").GetInt32()).ToArray();

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_NameReadOnlyHintAndSchema()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("ado_bridge_wit_history");
        JsonSerializer.SerializeToElement(tool.Annotations).GetProperty("readOnlyHint").GetBoolean()
            .Should().BeTrue();
        tool.Description.Should().Contain("newest first").And.Contain("ado_bridge_download_field");

        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["organization", "project", "id"]);
        var props = schema.GetProperty("properties");
        foreach (var p in new[] { "organization", "project", "id", "top", "skip" })
            props.TryGetProperty(p, out _).Should().BeTrue(p);
    }

    // ── projection ───────────────────────────────────────────────────────────

    [Fact]
    public async Task History_ProjectsFieldChanges_NewestFirst()
    {
        ReturnsPage(0, Page([
            Update(1, 1, "{\"System.Title\":{\"newValue\":\"First title\"},\"System.State\":{\"newValue\":\"New\"}}"),
            Update(2, 2, "{\"System.State\":{\"oldValue\":\"New\",\"newValue\":\"Active\"}," +
                $"\"Microsoft.VSTS.Common.ActivatedBy\":{{\"newValue\":{Identity("User B")}}}}}"),
            Update(3, 3, "{\"System.Title\":{\"oldValue\":\"First title\",\"newValue\":\"Second title\"}," +
                "\"Microsoft.VSTS.Scheduling.StoryPoints\":{\"oldValue\":3,\"newValue\":5}}"),
        ]));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42 });

        Revs(root).Should().Equal(3, 2, 1);

        var newest = root[0];
        newest.GetProperty("revisedBy").GetString().Should().Be("User A");
        newest.GetProperty("revisedDate").GetString().Should().Be("2026-01-04T10:00:00Z");
        newest.TryGetProperty("relations", out _).Should().BeFalse();
        var changes = newest.GetProperty("changes");
        changes.GetArrayLength().Should().Be(2);
        changes[0].GetProperty("field").GetString().Should().Be("System.Title");
        changes[0].GetProperty("old").GetString().Should().Be("First title");
        changes[0].GetProperty("new").GetString().Should().Be("Second title");
        changes[1].GetProperty("field").GetString().Should().Be("Microsoft.VSTS.Scheduling.StoryPoints");
        changes[1].GetProperty("old").GetInt32().Should().Be(3);
        changes[1].GetProperty("new").GetInt32().Should().Be(5);

        // A field set for the first time has no oldValue → old is null; identity values pass through.
        var oldest = root[2].GetProperty("changes");
        oldest[0].GetProperty("old").ValueKind.Should().Be(JsonValueKind.Null);
        oldest[0].GetProperty("new").GetString().Should().Be("First title");
        root[1].GetProperty("changes")[1].GetProperty("new").GetProperty("displayName").GetString()
            .Should().Be("User B");
    }

    [Fact]
    public async Task History_LinkOnlyUpdate_CarriesAdoRelationsVerbatim_WithNoFieldChanges()
    {
        // Live: link changes made from the other end arrive as extra updates that reuse the
        // current rev and carry no 'fields', only 'relations' { added / removed }.
        const string relations =
            "{\"added\":[{\"rel\":\"System.LinkTypes.Hierarchy-Forward\",\"url\":\"https://x/workItems/43\"," +
            "\"attributes\":{\"isLocked\":false,\"name\":\"Child\"}}]," +
            "\"removed\":[{\"rel\":\"System.LinkTypes.Related\",\"url\":\"https://x/workItems/44\"," +
            "\"attributes\":{\"isLocked\":false,\"name\":\"Related\"}}]}";
        ReturnsPage(0, Page([
            Update(1, 1, "{\"System.State\":{\"newValue\":\"New\"}}"),
            Update(2, 1, relations: relations),
        ]));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42 });

        Revs(root).Should().Equal(1, 1);
        root[0].GetProperty("changes").GetArrayLength().Should().Be(0);
        var rel = root[0].GetProperty("relations");
        rel.GetProperty("added")[0].GetProperty("rel").GetString().Should().Be("System.LinkTypes.Hierarchy-Forward");
        rel.GetProperty("added")[0].GetProperty("url").GetString().Should().Be("https://x/workItems/43");
        rel.GetProperty("removed")[0].GetProperty("attributes").GetProperty("name").GetString().Should().Be("Related");
        root[1].TryGetProperty("relations", out _).Should().BeFalse();
    }

    [Fact]
    public async Task History_MissingRevisedBy_IsNull()
    {
        ReturnsPage(0, "{\"count\":1,\"value\":[{\"id\":1,\"workItemId\":42,\"rev\":1}]}");

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42 });

        root[0].GetProperty("revisedBy").ValueKind.Should().Be(JsonValueKind.Null);
        root[0].GetProperty("revisedDate").ValueKind.Should().Be(JsonValueKind.Null);
        root[0].GetProperty("changes").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"id\":\"00000000-0000-0000-0000-000000000001\"}")]
    public async Task History_RevisedByWithoutDisplayName_IsNull(string revisedBy)
    {
        ReturnsPage(0, $"{{\"count\":1,\"value\":[{{\"id\":1,\"rev\":1,\"revisedBy\":{revisedBy}}}]}}");

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42 });

        root[0].GetProperty("revisedBy").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task History_PageWithoutValue_IsTreatedAsEmpty()
    {
        ReturnsPage(0, "{}");

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42 });

        root.GetArrayLength().Should().Be(0);
    }

    // ── Review Focus 5: long-text revisions are stubbed, never inlined ──────

    [Theory]
    [InlineData("oldValue")]
    [InlineData("newValue")]
    public async Task History_HugeLongTextChange_IsStubbed_NotInlined(string side)
    {
        var huge = new string('x', WorkItemSlimProjector.OversizeFieldCharCeiling + 1);
        ReturnsPage(0, Page([
            Update(1, 1, $"{{\"System.Description\":{{\"{side}\":\"{huge}\"}}}}"),
        ]));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", id = 42 }), default);

        result.IsError.Should().BeFalse();
        result.Text.Should().NotContain(huge);
        var change = Parse(result.Text)[0].GetProperty("changes")[0];
        var stub = change.GetProperty(side == "oldValue" ? "old" : "new");
        stub.GetProperty("stubbed").GetBoolean().Should().BeTrue();
        stub.GetProperty("length").GetInt32().Should().Be(4097);
    }

    [Theory]
    [InlineData("oldValue")]
    [InlineData("newValue")]
    public async Task History_LongTextAtCeiling_IsKeptInline(string side)
    {
        var atCeiling = new string('y', WorkItemSlimProjector.OversizeFieldCharCeiling);
        ReturnsPage(0, Page([
            Update(1, 1, $"{{\"System.Description\":{{\"{side}\":\"{atCeiling}\"}}}}"),
        ]));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42 });

        root[0].GetProperty("changes")[0].GetProperty(side == "oldValue" ? "old" : "new").GetString()
            .Should().HaveLength(4096);
    }

    // ── paging: top / skip ───────────────────────────────────────────────────

    [Fact]
    public async Task History_DefaultTop_Is20_NewestRevisions()
    {
        ReturnsPage(0, Simple(1, 25));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42 });

        Revs(root).Should().Equal(Enumerable.Range(6, 20).Reverse());
    }

    [Fact]
    public async Task History_TopOfOne_IsAccepted()
    {
        ReturnsPage(0, Simple(1, 5));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42, top = 1 });

        Revs(root).Should().Equal(5);
    }

    [Fact]
    public async Task History_TopAtMax_Returns100()
    {
        ReturnsPage(0, Simple(1, 150));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42, top = 100 });

        Revs(root).Should().Equal(Enumerable.Range(51, 100).Reverse());
    }

    [Fact]
    public async Task History_TopAboveMax_Clamped()
    {
        ReturnsPage(0, Simple(1, 150));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42, top = 101 });

        root.GetArrayLength().Should().Be(WitHistoryTool.MaxTop);
        Revs(root)[0].Should().Be(150);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task History_TopBelowOne_ThrowsCallerArgument(int top)
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", id = 42, top }), default);

        await act.Should().ThrowAsync<CallerArgumentException>().WithMessage("*'top'*");
        await _ado.DidNotReceiveWithAnyArgs().GetWorkItemUpdatesAsync(default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task History_Skip_SkipsNewestRevisions()
    {
        ReturnsPage(0, Simple(1, 10));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42, top = 3, skip = 2 });

        Revs(root).Should().Equal(8, 7, 6);
    }

    [Fact]
    public async Task History_SkipPastEnd_ReturnsEmptyArray()
    {
        ReturnsPage(0, Simple(1, 3));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42, skip = 3 });

        root.ValueKind.Should().Be(JsonValueKind.Array);
        root.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task History_NegativeSkip_ThrowsCallerArgument()
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", id = 42, skip = -1 }), default);

        await act.Should().ThrowAsync<CallerArgumentException>().WithMessage("*'skip'*");
    }

    [Fact]
    public async Task History_FetchesEveryAdoPage_BeforeOrderingNewestFirst()
    {
        // ADO returns updates oldest first and caps $top at 200, so the newest revisions
        // are only reachable by reading every page.
        ReturnsPage(0, Simple(1, 200));
        ReturnsPage(200, Simple(201, 230));

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42, top = 2 });

        Revs(root).Should().Equal(230, 229);
        await _ado.Received(2).GetWorkItemUpdatesAsync(
            "org", "proj", 42, WitHistoryTool.AdoPageSize, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task History_EmptyHistory_ReturnsEmptyArray()
    {
        ReturnsPage(0, "{\"count\":0,\"value\":[]}");

        var root = await InvokeOk(new { organization = "org", project = "proj", id = 42 });

        root.GetArrayLength().Should().Be(0);
    }

    // ── errors ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task History_ItemNotFound_ReturnsIsError()
    {
        _ado.GetWorkItemUpdatesAsync("org", "proj", 42, WitHistoryTool.AdoPageSize, 0, Arg.Any<CancellationToken>())
            .ThrowsAsync(new AdoRestException(404,
                "TF401232: Work item 42 does not exist, or you do not have permissions to read it."));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", id = 42 }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("404").And.Contain("TF401232: Work item 42 does not exist");
    }

    [Fact]
    public async Task History_TransportFailure_ReturnsIsError()
    {
        _ado.GetWorkItemUpdatesAsync(default!, default!, default, default, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", id = 42 }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("transport").And.Contain("connection reset");
    }

    [Fact]
    public async Task History_MissingId_ThrowsCallerArgument()
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj" }), default);

        await act.Should().ThrowAsync<CallerArgumentException>().WithMessage("*'id'*");
    }

    [Fact]
    public async Task History_NonPositiveId_ThrowsCallerArgument()
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", id = 0 }), default);

        await act.Should().ThrowAsync<CallerArgumentException>().WithMessage("*'id'*");
    }
}
