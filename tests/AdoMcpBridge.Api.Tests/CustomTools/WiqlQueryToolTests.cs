using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WiqlQueryToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private WiqlQueryTool CreateTool() => new(_ado, NullLogger<WiqlQueryTool>.Instance);

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Name_is_the_bridge_native_tool_id()
    {
        CreateTool().Name.Should().Be("ado_bridge_wiql_query");
    }

    [Fact]
    public void Annotations_advertise_read_only_hint()
    {
        var json = JsonSerializer.Serialize(CreateTool().Annotations);
        JsonDocument.Parse(json).RootElement.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Description_states_the_wit_query_gap_and_id_only_hydration_contract()
    {
        var description = CreateTool().Description;

        description.Should().Contain("ad-hoc WIQL");
        description.Should().Contain("wit_query");
        description.Should().Contain("saved queries");
        description.Should().Contain("ado_bridge_wit_get_batch");
        description.Should().Contain("@CurrentIteration");
    }

    [Fact]
    public void InputSchema_serializes_with_the_required_fields()
    {
        var json = JsonSerializer.Serialize(CreateTool().InputSchema);
        var root = JsonDocument.Parse(json).RootElement;

        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.Should().BeEquivalentTo(["organization", "wiql"]);

        var props = root.GetProperty("properties");
        props.TryGetProperty("organization", out _).Should().BeTrue();
        props.TryGetProperty("wiql", out _).Should().BeTrue();
        props.TryGetProperty("project", out _).Should().BeTrue();
        props.TryGetProperty("team", out _).Should().BeTrue();
        props.TryGetProperty("top", out _).Should().BeTrue();
        props.TryGetProperty("timePrecision", out _).Should().BeTrue();
    }

    // ── flat happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_projects_a_flat_result_to_slim_json()
    {
        _ado.QueryByWiqlAsync("org", null, null, "SELECT [System.Id] FROM WorkItems",
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(FlatResult([101, 102], ["System.Id", "System.Title"]));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", wiql = "SELECT [System.Id] FROM WorkItems" }), default);

        result.IsError.Should().BeFalse();
        var root = JsonDocument.Parse(result.Text).RootElement;
        root.GetProperty("queryType").GetString().Should().Be("flat");
        root.GetProperty("queryResultType").GetString().Should().Be("workItem");
        root.GetProperty("asOf").GetString().Should().Be("2026-08-02T00:00:00Z");
        root.GetProperty("columns").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["System.Id", "System.Title"]);
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        root.GetProperty("workItems").EnumerateArray().Select(e => e.GetInt32())
            .Should().BeEquivalentTo([101, 102]);
    }

    [Fact]
    public async Task InvokeAsync_applies_default_top_of_200_requesting_one_extra()
    {
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(FlatResult([], []));

        await CreateTool().InvokeAsync(Args(new { organization = "org", wiql = "q" }), default);

        await _ado.Received().QueryByWiqlAsync("org", null, null, "q", 201, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_forwards_explicit_top_project_team_and_timePrecision()
    {
        _ado.QueryByWiqlAsync("org", "proj", "team", "q",
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(FlatResult([1], ["System.Id"]));

        await CreateTool().InvokeAsync(Args(new
        {
            organization = "org",
            wiql = "q",
            project = "proj",
            team = "team",
            top = 50,
            timePrecision = true,
        }), default);

        await _ado.Received().QueryByWiqlAsync("org", "proj", "team", "q", 51, true, Arg.Any<CancellationToken>());
    }

    // ── link happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_projects_a_link_result_with_rel_source_target_ids()
    {
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(LinkResult(
            [
                (rel: "System.LinkTypes.Hierarchy-Forward", source: 101, target: 102),
                (rel: null, source: null, target: 103),
            ]));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", wiql = "q" }), default);

        result.IsError.Should().BeFalse();
        var root = JsonDocument.Parse(result.Text).RootElement;
        root.GetProperty("queryResultType").GetString().Should().Be("workItemLink");
        root.TryGetProperty("workItems", out _).Should().BeFalse();
        var relations = root.GetProperty("workItemRelations").EnumerateArray().ToList();
        relations.Should().HaveCount(2);

        relations[0].GetProperty("rel").GetString().Should().Be("System.LinkTypes.Hierarchy-Forward");
        relations[0].GetProperty("source").GetInt32().Should().Be(101);
        relations[0].GetProperty("target").GetInt32().Should().Be(102);

        relations[1].GetProperty("rel").ValueKind.Should().Be(JsonValueKind.Null);
        relations[1].GetProperty("source").ValueKind.Should().Be(JsonValueKind.Null);
        relations[1].GetProperty("target").GetInt32().Should().Be(103);
    }

    [Fact]
    public async Task InvokeAsync_writes_null_target_when_relation_target_is_absent()
    {
        // Defensive: ADO normally always populates target, but a null must not throw.
        var json = JsonSerializer.Serialize(new
        {
            queryType = "tree",
            queryResultType = "workItemLink",
            columns = Array.Empty<object>(),
            workItemRelations = new[]
            {
                new { rel = (string?)null, source = (object?)null, target = (object?)null },
            },
        });
        using var doc = JsonDocument.Parse(json);
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(doc.RootElement.Clone());

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", wiql = "q" }), default);

        var rel = JsonDocument.Parse(result.Text).RootElement
            .GetProperty("workItemRelations").EnumerateArray().First();
        rel.GetProperty("target").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task InvokeAsync_tolerates_a_minimal_flat_result_missing_envelope_fields()
    {
        // A degenerate response with no queryType/queryResultType/asOf/columns/workItems:
        // the envelope fields are omitted, columns is empty, and the flat branch yields no ids.
        using var doc = JsonDocument.Parse("{}");
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(doc.RootElement.Clone());

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", wiql = "q" }), default);

        var root = JsonDocument.Parse(result.Text).RootElement;
        root.TryGetProperty("queryType", out _).Should().BeFalse();
        root.TryGetProperty("queryResultType", out _).Should().BeFalse();
        root.TryGetProperty("asOf", out _).Should().BeFalse();
        root.GetProperty("columns").GetArrayLength().Should().Be(0);
        root.GetProperty("count").GetInt32().Should().Be(0);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        root.GetProperty("workItems").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_skips_columns_missing_a_referenceName()
    {
        var json = JsonSerializer.Serialize(new
        {
            queryType = "flat",
            queryResultType = "workItem",
            columns = new object[]
            {
                new { referenceName = "System.Id", name = "ID" },
                new { name = "orphan-no-refname" },
            },
            workItems = Array.Empty<object>(),
        });
        using var doc = JsonDocument.Parse(json);
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(doc.RootElement.Clone());

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", wiql = "q" }), default);

        JsonDocument.Parse(result.Text).RootElement.GetProperty("columns")
            .EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["System.Id"]);
    }

    // ── truncation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_trims_and_flags_truncation_for_flat_results()
    {
        // effectiveTop = 2; ADO returns 3 (the requested top+1) → trim to 2, flag truncated.
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(FlatResult([1, 2, 3], ["System.Id"]));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", wiql = "q", top = 2 }), default);

        var root = JsonDocument.Parse(result.Text).RootElement;
        root.GetProperty("truncated").GetBoolean().Should().BeTrue();
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("workItems").EnumerateArray().Select(e => e.GetInt32())
            .Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task InvokeAsync_trims_and_flags_truncation_for_link_results()
    {
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(LinkResult(
            [
                (rel: "r", source: 1, target: 2),
                (rel: "r", source: 3, target: 4),
                (rel: "r", source: 5, target: 6),
            ]));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", wiql = "q", top = 2 }), default);

        var root = JsonDocument.Parse(result.Text).RootElement;
        root.GetProperty("truncated").GetBoolean().Should().BeTrue();
        root.GetProperty("count").GetInt32().Should().Be(2);
        root.GetProperty("workItemRelations").GetArrayLength().Should().Be(2);
    }

    // ── validation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_rejects_missing_organization()
    {
        var result = await CreateTool().InvokeAsync(Args(new { organization = "", wiql = "q" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("organization is required");
        await _ado.DidNotReceiveWithAnyArgs().QueryByWiqlAsync(
            default!, default, default, default!, default, default, default);
    }

    [Fact]
    public async Task InvokeAsync_rejects_missing_wiql()
    {
        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", wiql = "  " }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("wiql is required");
    }

    [Fact]
    public async Task InvokeAsync_rejects_team_without_project()
    {
        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", wiql = "q", team = "team" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("team is only valid together with project");
    }

    [Fact]
    public async Task InvokeAsync_rejects_top_below_one()
    {
        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", wiql = "q", top = 0 }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("top must be between 1 and 2000");
    }

    [Fact]
    public async Task InvokeAsync_rejects_top_above_max()
    {
        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", wiql = "q", top = 2001 }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("top must be between 1 and 2000");
    }

    // ── error surfacing ──────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_surfaces_the_ado_wiql_error_message()
    {
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns<JsonElement>(_ => throw new AdoWiqlQueryException(
                "TF51005: The query references a field that does not exist. The error is caused by «[System.Bogus]»."));

        var result = await CreateTool().InvokeAsync(
            Args(new { organization = "org", wiql = "SELECT [System.Bogus] FROM WorkItems" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("TF51005");
        result.Text.Should().Contain("[System.Bogus]");
    }

    [Fact]
    public async Task InvokeAsync_returns_generic_error_on_transport_failure()
    {
        _ado.QueryByWiqlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns<JsonElement>(_ => throw new HttpRequestException("connection reset"));

        var result = await CreateTool().InvokeAsync(Args(new { organization = "org", wiql = "q" }), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static JsonElement Args(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    private static JsonElement FlatResult(int[] ids, string[] columnRefNames)
    {
        var json = JsonSerializer.Serialize(new
        {
            queryType = "flat",
            queryResultType = "workItem",
            asOf = "2026-08-02T00:00:00Z",
            columns = columnRefNames.Select(r => new
            {
                referenceName = r,
                name = r,
                url = $"https://example/{r}",
            }),
            workItems = ids.Select(id => new { id, url = $"https://example/{id}" }),
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement LinkResult((string? rel, int? source, int target)[] relations)
    {
        var json = JsonSerializer.Serialize(new
        {
            queryType = "tree",
            queryResultType = "workItemLink",
            asOf = "2026-08-02T00:00:00Z",
            columns = new[] { new { referenceName = "System.Id", name = "ID", url = "https://example/id" } },
            workItemRelations = relations.Select(r => new
            {
                rel = r.rel,
                source = r.source is null ? null : new { id = r.source.Value, url = $"https://example/{r.source}" },
                target = new { id = r.target, url = $"https://example/{r.target}" },
            }),
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
