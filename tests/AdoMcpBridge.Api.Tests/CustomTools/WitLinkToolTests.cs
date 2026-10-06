using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitLinkToolTests
{
    // Shape observed live: ADO returns relation urls carrying the project GUID, not its name.
    private const string ProjectGuid = "501e25ca-92f7-4a42-8466-96be2763c5d8";

    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();
    private string? _sentOps;

    public WitLinkToolTests()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ReturnsForAnyArgs(ci =>
            {
                _sentOps = JsonSerializer.Serialize(ci.ArgAt<IReadOnlyList<object>>(3));
                return Parse("{\"id\":42,\"rev\":8}");
            });
    }

    private WitLinkTool CreateTool() => new(_ado, NullLogger<WitLinkTool>.Instance);

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string RelUrl(int id) => $"https://dev.azure.com/my-org/{ProjectGuid}/_apis/wit/workItems/{id}";

    private void GivenRelations(int rev, params (string Rel, string Url)[] relations)
    {
        var json = JsonSerializer.Serialize(new
        {
            id = 42,
            rev,
            relations = relations.Select(r => new { rel = r.Rel, url = r.Url, attributes = new { isLocked = false } }),
        });
        _ado.GetWorkItemAsync(default!, default!, default, default).ReturnsForAnyArgs(Parse(json));
    }

    private Task<McpToolResult> Invoke(string action, string linkType, int targetId = 7, string? comment = null)
        => CreateTool().InvokeAsync(
            Args(new { organization = "my-org", project = "proj", id = 42, targetId, action, linkType, comment }),
            default);

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_NameWriteHintAndSchema()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("ado_bridge_wit_link");
        JsonSerializer.SerializeToElement(tool.Annotations).GetProperty("readOnlyHint").GetBoolean()
            .Should().BeFalse();
        tool.Description.Should().Contain("parent").And.Contain("System.Parent");

        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["organization", "project", "id", "targetId", "action", "linkType"]);
        var props = schema.GetProperty("properties");
        props.GetProperty("action").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("add", "remove");
        props.GetProperty("linkType").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("parent", "child", "related", "predecessor", "successor", "duplicate-of", "duplicate");
    }

    // ── add ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Link_Add_Parent_BuildsHierarchyReverseOp()
    {
        var result = await Invoke("add", "parent");

        result.IsError.Should().BeFalse();
        _sentOps.Should().Be(
            "[{\"op\":\"add\",\"path\":\"/relations/-\",\"value\":{\"rel\":\"System.LinkTypes.Hierarchy-Reverse\"," +
            "\"url\":\"https://dev.azure.com/my-org/_apis/wit/workItems/7\"}}]");
        await _ado.Received(1).UpdateWorkItemAsync("my-org", "proj", 42, Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>());
        await _ado.DidNotReceiveWithAnyArgs().GetWorkItemAsync(default!, default!, default, default);
    }

    [Theory]
    [InlineData("parent", "System.LinkTypes.Hierarchy-Reverse")]
    [InlineData("child", "System.LinkTypes.Hierarchy-Forward")]
    [InlineData("related", "System.LinkTypes.Related")]
    [InlineData("predecessor", "System.LinkTypes.Dependency-Reverse")]
    [InlineData("successor", "System.LinkTypes.Dependency-Forward")]
    [InlineData("duplicate-of", "System.LinkTypes.Duplicate-Reverse")]
    [InlineData("duplicate", "System.LinkTypes.Duplicate-Forward")]
    public async Task Link_Add_MapsEachLinkTypeToItsRelName(string linkType, string rel)
    {
        await Invoke("add", linkType);

        Parse(_sentOps!)[0].GetProperty("value").GetProperty("rel").GetString().Should().Be(rel);
    }

    [Fact]
    public async Task Link_Add_IncludesCommentAttribute()
    {
        await Invoke("add", "related", comment: "blocked by \"infra\"");

        var value = Parse(_sentOps!)[0].GetProperty("value");
        value.GetProperty("attributes").GetProperty("comment").GetString().Should().Be("blocked by \"infra\"");
    }

    [Fact]
    public async Task Link_Add_TargetUrl_UsesOrganizationEscaped_AndNoProject()
    {
        await CreateTool().InvokeAsync(
            Args(new { organization = "my org", project = "Some Project", id = 42, targetId = 99, action = "add", linkType = "child" }),
            default);

        Parse(_sentOps!)[0].GetProperty("value").GetProperty("url").GetString()
            .Should().Be("https://dev.azure.com/my%20org/_apis/wit/workItems/99");
        await _ado.Received(1).UpdateWorkItemAsync("my org", "Some Project", 42, Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Link_Add_ReturnsLinkedStatus()
    {
        var result = await Invoke("add", "duplicate-of", targetId: 9);

        var root = Parse(result.Text);
        root.GetProperty("status").GetString().Should().Be("LINKED");
        root.GetProperty("id").GetInt32().Should().Be(42);
        root.GetProperty("targetId").GetInt32().Should().Be(9);
        root.GetProperty("linkType").GetString().Should().Be("duplicate-of");
    }

    // ── remove ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Link_Remove_FindsIndexAndEmitsRemove()
    {
        GivenRelations(5,
            ("System.LinkTypes.Hierarchy-Reverse", RelUrl(1)),
            ("System.LinkTypes.Related", RelUrl(7)));

        var result = await Invoke("remove", "related");

        result.IsError.Should().BeFalse();
        _sentOps.Should().Be(
            "[{\"op\":\"test\",\"path\":\"/rev\",\"value\":5},{\"op\":\"remove\",\"path\":\"/relations/1\"}]");
        await _ado.Received(1).GetWorkItemAsync("my-org", "proj", 42, Arg.Any<CancellationToken>());
        var root = Parse(result.Text);
        root.GetProperty("status").GetString().Should().Be("UNLINKED");
        root.GetProperty("targetId").GetInt32().Should().Be(7);
        root.GetProperty("linkType").GetString().Should().Be("related");
    }

    [Fact]
    public async Task Link_Remove_SameRelToDifferentTargets_PicksTheMatchingTarget()
    {
        GivenRelations(3,
            ("System.LinkTypes.Related", RelUrl(70)),
            ("System.LinkTypes.Related", RelUrl(17)),
            ("System.LinkTypes.Related", RelUrl(7)),
            ("System.LinkTypes.Related", RelUrl(77)));

        await Invoke("remove", "related", targetId: 7);

        Parse(_sentOps!)[1].GetProperty("path").GetString().Should().Be("/relations/2");
    }

    [Fact]
    public async Task Link_Remove_DifferentRelToSameTarget_PicksTheMatchingRel()
    {
        GivenRelations(3,
            ("System.LinkTypes.Related", RelUrl(7)),
            ("System.LinkTypes.Dependency-Forward", RelUrl(7)),
            ("System.LinkTypes.Dependency-Reverse", RelUrl(7)));

        await Invoke("remove", "predecessor", targetId: 7);

        Parse(_sentOps!)[1].GetProperty("path").GetString().Should().Be("/relations/2");
    }

    [Fact]
    public async Task Link_Remove_IgnoresNonWorkItemUrlsWithMatchingTrailingNumber()
    {
        GivenRelations(3,
            ("System.LinkTypes.Related", "vstfs:///Git/Commit/7"),
            ("System.LinkTypes.Related", "https://dev.azure.com/my-org/_apis/wit/attachments/7"),
            ("System.LinkTypes.Related", RelUrl(7) + "0"),
            ("System.LinkTypes.Related", RelUrl(7)));

        await Invoke("remove", "related", targetId: 7);

        Parse(_sentOps!)[1].GetProperty("path").GetString().Should().Be("/relations/3");
    }

    [Fact]
    public async Task Link_Remove_NotPresent_ReturnsIsError_DoesNotPatch()
    {
        GivenRelations(3,
            ("System.LinkTypes.Related", RelUrl(8)),
            ("System.LinkTypes.Hierarchy-Reverse", RelUrl(7)));

        var result = await Invoke("remove", "related", targetId: 7);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("related").And.Contain("42").And.Contain("7").And.Contain("nothing");
        await NoWrite();
    }

    [Fact]
    public async Task Link_Remove_ItemWithNoRelations_ReturnsIsError_DoesNotPatch()
    {
        _ado.GetWorkItemAsync(default!, default!, default, default).ReturnsForAnyArgs(Parse("{\"id\":42,\"rev\":1}"));

        var result = await Invoke("remove", "parent");

        result.IsError.Should().BeTrue();
        await NoWrite();
    }

    [Fact]
    public async Task Link_Remove_SkipsMalformedRelationEntries()
    {
        _ado.GetWorkItemAsync(default!, default!, default, default).ReturnsForAnyArgs(Parse(
            "{\"id\":42,\"rev\":2,\"relations\":[" +
            "\"junk\"," +
            "{\"rel\":\"System.LinkTypes.Related\"}," +
            "{\"rel\":\"System.LinkTypes.Related\",\"url\":5}," +
            "{\"rel\":5,\"url\":\"" + RelUrl(7) + "\"}," +
            "{\"rel\":\"System.LinkTypes.Related\",\"url\":\"https://dev.azure.com/my-org/_apis/wit/workItems/abc\"}," +
            "{\"rel\":\"System.LinkTypes.Related\",\"url\":\"" + RelUrl(7) + "\"}]}"));

        await Invoke("remove", "related", targetId: 7);

        Parse(_sentOps!)[1].GetProperty("path").GetString().Should().Be("/relations/5");
    }

    [Fact]
    public async Task Link_Remove_RelationsNotAnArray_ReturnsIsError_DoesNotPatch()
    {
        _ado.GetWorkItemAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Parse("{\"id\":42,\"rev\":2,\"relations\":{}}"));

        var result = await Invoke("remove", "related");

        result.IsError.Should().BeTrue();
        await NoWrite();
    }

    [Fact]
    public async Task Link_Remove_NoRevInRead_OmitsTestOp()
    {
        _ado.GetWorkItemAsync(default!, default!, default, default).ReturnsForAnyArgs(Parse(
            "{\"id\":42,\"relations\":[{\"rel\":\"System.LinkTypes.Related\",\"url\":\"" + RelUrl(7) + "\"}]}"));

        await Invoke("remove", "related");

        _sentOps.Should().Be("[{\"op\":\"remove\",\"path\":\"/relations/0\"}]");
    }

    [Fact]
    public async Task Link_Remove_WorkItemNotFound_ReturnsIsError_DoesNotPatch()
    {
        _ado.GetWorkItemAsync(default!, default!, default, default).ReturnsForAnyArgs((JsonElement?)null);

        var result = await Invoke("remove", "related");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("42").And.Contain("not found");
        await NoWrite();
    }

    [Fact]
    public async Task Link_Remove_IgnoresComment()
    {
        GivenRelations(3, ("System.LinkTypes.Related", RelUrl(7)));

        await Invoke("remove", "related", comment: "why");

        _sentOps.Should().NotContain("why");
    }

    // ── read failures (nothing written) ──────────────────────────────────────

    [Fact]
    public async Task Link_Remove_ReadAdoRestException_ReturnsIsError_DoesNotPatch()
    {
        _ado.GetWorkItemAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(403, "TF401019: no access"));

        var result = await Invoke("remove", "related");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 403").And.Contain("TF401019");
        await NoWrite();
    }

    [Fact]
    public async Task Link_Remove_ReadHttpRequestException_ReturnsIsError_DoesNotPatch()
    {
        _ado.GetWorkItemAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("connection reset"));

        var result = await Invoke("remove", "related");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("connection reset");
        await NoWrite();
    }

    [Fact]
    public async Task Link_Remove_ReadTimeout_ReturnsIsError_SayingNothingWritten()
    {
        _ado.GetWorkItemAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new TaskCanceledException("HttpClient.Timeout"));

        var result = await Invoke("remove", "related");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("timed out").And.Contain("nothing was written");
        await NoWrite();
    }

    // ── argument validation (Review Focus 4) ─────────────────────────────────

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task Link_ToSelf_Throws(string action)
    {
        var act = () => Invoke(action, "related", targetId: 42);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("itself");
        await NoWrite();
        await _ado.DidNotReceiveWithAnyArgs().GetWorkItemAsync(default!, default!, default, default);
    }

    [Theory]
    [InlineData("blocks")]
    [InlineData("Parent")]
    [InlineData("System.LinkTypes.Related")]
    [InlineData("")]
    public async Task Link_UnknownLinkType_ThrowsListingValidTypes(string linkType)
    {
        var act = () => Invoke("add", linkType);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("linkType")
            .And.Contain("parent, child, related, predecessor, successor, duplicate-of, duplicate");
        await NoWrite();
    }

    [Fact]
    public async Task Link_NonStringLinkType_ThrowsListingValidTypes()
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "o", project = "p", id = 1, targetId = 2, action = "add", linkType = 3 }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("duplicate-of");
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("ADD")]
    public async Task Link_UnknownAction_Throws(string action)
    {
        var act = () => Invoke(action, "related");

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("action").And.Contain("add").And.Contain("remove");
        await NoWrite();
    }

    [Fact]
    public async Task Link_MissingTargetId_Throws()
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "o", project = "p", id = 1, action = "add", linkType = "related" }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("targetId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Link_NonPositiveTargetId_Throws(int targetId)
    {
        var act = () => Invoke("add", "related", targetId: targetId);

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("targetId");
        await NoWrite();
    }

    // ── write failures ───────────────────────────────────────────────────────

    [Fact]
    public async Task Link_AdoRestException_ReturnsIsError()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(400,
                "TF201036: You cannot add a Child link between work items 42 and 7 which already has a Parent link."));

        var result = await Invoke("add", "child");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 400").And.Contain("TF201036");
    }

    [Fact]
    public async Task Link_HttpRequestException_ReturnsIsError()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("connection reset"));

        var result = await Invoke("add", "related");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("connection reset");
    }

    [Fact]
    public async Task Link_Timeout_ReturnsIsError_SayingWriteMayHaveBeenApplied()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var result = await Invoke("add", "related");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("timed out").And.Contain("MAY have been applied").And.Contain("re-read");
    }

    [Fact]
    public async Task Link_NonJsonSuccessBody_ReturnsIsError_SayingWriteMayHaveBeenApplied()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new JsonException("'<' is an invalid start of a value."));

        var result = await Invoke("add", "related");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("MAY have been applied").And.Contain("re-read");
    }

    [Fact]
    public async Task Link_RealCallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new OperationCanceledException(cts.Token));

        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "o", project = "p", id = 1, targetId = 2, action = "add", linkType = "related" }),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private Task NoWrite()
        => _ado.DidNotReceiveWithAnyArgs().UpdateWorkItemAsync(default!, default!, default, default!, default);
}
