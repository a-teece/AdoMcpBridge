using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitRunQueryToolTests
{
    private const string QueryId = "8c070a12-8457-43f9-96d3-b281e23fabf5";
    private static readonly Guid QueryGuid = Guid.Parse(QueryId);

    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private WitRunQueryTool CreateTool() => new(_ado, NullLogger<WitRunQueryTool>.Instance);

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // GET _apis/wit/wiql/{id} returns the same WorkItemQueryResult as POST _apis/wit/wiql.
    private const string FlatThree =
        "{\"queryType\":\"flat\",\"queryResultType\":\"workItem\",\"asOf\":\"2026-10-06T00:00:00Z\"," +
        "\"columns\":[{\"referenceName\":\"System.Id\",\"name\":\"ID\",\"url\":\"https://x/id\"}]," +
        "\"workItems\":[{\"id\":11,\"url\":\"https://x/11\"},{\"id\":12,\"url\":\"https://x/12\"}," +
        "{\"id\":13,\"url\":\"https://x/13\"}]}";

    private void ReturnsForAnyRun(string json)
        => _ado.RunSavedQueryAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(Parse(json));

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_NameReadOnlyHintAndSchema()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("ado_bridge_wit_run_query");
        JsonSerializer.SerializeToElement(tool.Annotations).GetProperty("readOnlyHint").GetBoolean()
            .Should().BeTrue();
        tool.Description.Should().Contain("ado_bridge_wit_list_queries")
            .And.Contain("ado_bridge_wit_get_batch");

        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["organization", "project", "queryId"]);
        var props = schema.GetProperty("properties");
        foreach (var p in new[] { "organization", "project", "queryId", "team", "top" })
            props.TryGetProperty(p, out _).Should().BeTrue(p);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunQuery_ReturnsIdsOnly_AndTruncatedFlag()
    {
        // top = 2 → request 3 so the extra row reveals truncation.
        _ado.RunSavedQueryAsync("org", "proj", null, QueryGuid, 3, Arg.Any<CancellationToken>())
            .Returns(Parse(FlatThree));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId = QueryId, top = 2 }), default);

        result.IsError.Should().BeFalse();
        var root = Parse(result.Text);
        root.GetProperty("queryType").GetString().Should().Be("flat");
        root.GetProperty("columns").EnumerateArray().Select(e => e.GetString()).Should().Equal("System.Id");
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeTrue();
        root.GetProperty("workItems").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(11, 12);
    }

    [Fact]
    public async Task RunQuery_LinkResult_IsSlimmedLikeWiqlQuery()
    {
        ReturnsForAnyRun(
            "{\"queryType\":\"tree\",\"queryResultType\":\"workItemLink\",\"columns\":[]," +
            "\"workItemRelations\":[{\"rel\":null,\"source\":null,\"target\":{\"id\":1,\"url\":\"u\"}}," +
            "{\"rel\":\"System.LinkTypes.Hierarchy-Forward\",\"source\":{\"id\":1},\"target\":{\"id\":2}}]}");

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId = QueryId }), default);

        var root = Parse(result.Text);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var rels = root.GetProperty("workItemRelations");
        rels.GetArrayLength().Should().Be(2);
        rels[1].GetProperty("rel").GetString().Should().Be("System.LinkTypes.Hierarchy-Forward");
        rels[1].GetProperty("source").GetInt32().Should().Be(1);
        rels[1].GetProperty("target").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task RunQuery_DefaultTop200_RequestsOneExtra_AndForwardsTeam()
    {
        ReturnsForAnyRun(FlatThree);

        await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId = QueryId, team = "my team" }), default);

        await _ado.Received(1).RunSavedQueryAsync("org", "proj", "my team", QueryGuid, 201, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunQuery_BlankTeam_IsTreatedAsAbsent()
    {
        ReturnsForAnyRun(FlatThree);

        await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId = QueryId, team = "  " }), default);

        await _ado.Received(1).RunSavedQueryAsync("org", "proj", null, QueryGuid, 201, Arg.Any<CancellationToken>());
    }

    // ── top bounds (1–2000) ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2000)]
    public async Task RunQuery_TopAtEitherBound_IsAccepted(int top)
    {
        ReturnsForAnyRun(FlatThree);

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId = QueryId, top }), default);

        result.IsError.Should().BeFalse();
        await _ado.Received(1).RunSavedQueryAsync("org", "proj", null, QueryGuid, top + 1, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2001)]
    public async Task RunQuery_TopOutsideBounds_ThrowsCallerArgumentException(int top)
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId = QueryId, top }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("top");
        await _ado.DidNotReceiveWithAnyArgs()
            .RunSavedQueryAsync(default!, default!, default, default, default, default);
    }

    // ── argument validation ──────────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("8c070a12-8457-43f9-96d3")]
    public async Task RunQuery_InvalidGuid_ThrowsCallerArgumentException(string queryId)
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("queryId").And.Contain("GUID");
        await _ado.DidNotReceiveWithAnyArgs()
            .RunSavedQueryAsync(default!, default!, default, default, default, default);
    }

    [Fact]
    public async Task RunQuery_MissingQueryId_ThrowsCallerArgumentException()
    {
        var act = () => CreateTool().InvokeAsync(Args(new { organization = "org", project = "proj" }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("queryId");
    }

    // ── ADO failures surface verbatim ────────────────────────────────────────

    [Fact]
    public async Task RunQuery_404_ReturnsIsErrorWithAdoMessage()
    {
        // Message captured live (2026-10-06) for an unknown query id.
        _ado.RunSavedQueryAsync(default!, default!, default, default, default, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(404,
                "TF401243: The query 00000000-0000-0000-0000-000000000001 does not exist, " +
                "or you do not have permission to read it."));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId = "00000000-0000-0000-0000-000000000001" }),
            default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 404").And.Contain("TF401243");
    }

    [Fact]
    public async Task RunQuery_HttpRequestException_ReturnsIsError()
    {
        _ado.RunSavedQueryAsync(default!, default!, default, default, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", queryId = QueryId }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("connection reset");
    }
}
