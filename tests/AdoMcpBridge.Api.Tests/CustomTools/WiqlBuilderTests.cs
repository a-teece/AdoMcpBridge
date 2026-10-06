using System.Text.RegularExpressions;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WiqlBuilderTests
{
    private const string DefaultOrder = "System.ChangedDate desc";

    // A WIQL single-quoted literal: '...' with embedded quotes doubled.
    private static readonly Regex Literal = new("'(?:[^']|'')*'", RegexOptions.CultureInvariant);

    private static WiqlFilter Filter(
        string project = "Agile Playground",
        IReadOnlyList<string>? types = null,
        IReadOnlyList<string>? states = null,
        string? assignedTo = null,
        string? areaPath = null,
        string? iterationPath = null,
        IReadOnlyList<string>? tags = null,
        string? textContains = null,
        string orderBy = DefaultOrder)
        => new(project, types, states, assignedTo, areaPath, iterationPath, tags, textContains, orderBy);

    // ── Quote ────────────────────────────────────────────────────────────────

    [Fact]
    public void Quote_EscapesSingleQuotes_ByDoubling()
    {
        WiqlBuilder.Quote("O'Brien").Should().Be("'O''Brien'");
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("", "''")]
    [InlineData("''", "''''''")]
    [InlineData("a \"b\" [c]", "'a \"b\" [c]'")]
    public void Quote_WrapsAndOnlyDoublesSingleQuotes(string value, string expected)
    {
        WiqlBuilder.Quote(value).Should().Be(expected);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AlwaysScopesToTeamProject()
    {
        WiqlBuilder.Build(Filter()).Should().Be(
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Agile Playground' " +
            "ORDER BY [System.ChangedDate] DESC");
    }

    [Fact]
    public void Build_AllFilters_EmitsEachClauseInFixedOrder()
    {
        var wiql = WiqlBuilder.Build(Filter(
            types: ["Bug", "Task"],
            states: ["Active"],
            assignedTo: "jo@example.com",
            areaPath: @"Agile Playground\Team A",
            iterationPath: @"Agile Playground\Sprint 1",
            tags: ["alpha", "beta"],
            textContains: "rain",
            orderBy: "Microsoft.VSTS.Common.Priority asc"));

        wiql.Should().Be(
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = 'Agile Playground'" +
            " AND [System.WorkItemType] IN ('Bug', 'Task')" +
            " AND [System.State] IN ('Active')" +
            " AND [System.AssignedTo] = 'jo@example.com'" +
            @" AND [System.AreaPath] UNDER 'Agile Playground\Team A'" +
            @" AND [System.IterationPath] UNDER 'Agile Playground\Sprint 1'" +
            " AND [System.Tags] CONTAINS 'alpha'" +
            " AND [System.Tags] CONTAINS 'beta'" +
            " AND [System.Title] CONTAINS 'rain'" +
            " ORDER BY [Microsoft.VSTS.Common.Priority] ASC");
    }

    [Fact]
    public void Build_EmptyLists_AreIgnored()
    {
        WiqlBuilder.Build(Filter(types: [], states: [], tags: []))
            .Should().Be(WiqlBuilder.Build(Filter()));
    }

    [Theory]
    [InlineData("@me")]
    [InlineData("@Me")]
    [InlineData("@ME")]
    public void Build_AssignedToMe_EmitsAtMe(string me)
    {
        var wiql = WiqlBuilder.Build(Filter(assignedTo: me));

        wiql.Should().Contain(" AND [System.AssignedTo] = @Me ");
        wiql.Should().NotContain("'@");
    }

    [Fact]
    public void Build_ValueWithQuoteAndBracket_DoesNotAlterQuery()
    {
        // Review Focus 1: ', " and ] in every user-supplied slot must stay inside a literal.
        const string evil = "x' OR [System.Id] > 0 --";
        const string evil2 = "a\"]) OR ([System.Id] > 0";

        var benign = WiqlBuilder.Build(Filter(
            project: "p", types: ["t", "t2"], states: ["s"], assignedTo: "u", areaPath: "a",
            iterationPath: "i", tags: ["g", "g2"], textContains: "c"));
        var hostile = WiqlBuilder.Build(Filter(
            project: evil, types: [evil, evil2], states: [evil2], assignedTo: evil, areaPath: evil2,
            iterationPath: evil, tags: [evil, evil2], textContains: evil));

        // Strip every literal: the remaining query skeleton must be identical, i.e. no user
        // value escaped its literal into WIQL syntax.
        Literal.Replace(hostile, "?").Should().Be(Literal.Replace(benign, "?"));
        Literal.Count(hostile).Should().Be(Literal.Count(benign));

        hostile.Should().Contain("'x'' OR [System.Id] > 0 --'");
        hostile.Should().Contain("'a\"]) OR ([System.Id] > 0'");
    }

    [Theory]
    [InlineData("System.ChangedDate", "[System.ChangedDate] ASC")] // bare field: WIQL default direction
    [InlineData("System.CreatedDate asc", "[System.CreatedDate] ASC")]
    [InlineData("system.id DESC", "[System.Id] DESC")]
    [InlineData("  Microsoft.VSTS.Common.StackRank   Asc ", "[Microsoft.VSTS.Common.StackRank] ASC")]
    [InlineData("Microsoft.VSTS.Common.Priority desc", "[Microsoft.VSTS.Common.Priority] DESC")]
    public void Build_WhitelistedOrderBy_EmitsCanonicalField(string orderBy, string expected)
    {
        WiqlBuilder.Build(Filter(orderBy: orderBy)).Should().EndWith(" ORDER BY " + expected);
    }

    [Theory]
    [InlineData("System.Title")]
    [InlineData("System.ChangedDate sideways")]
    [InlineData("System.ChangedDate desc, System.Id")]
    [InlineData("[System.Id]")]
    [InlineData("System.Id desc extra")]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_OrderByNotWhitelisted_Throws(string orderBy)
    {
        var act = () => WiqlBuilder.Build(Filter(orderBy: orderBy));

        act.Should().Throw<CallerArgumentException>().Which.Message.Should().Contain("orderBy");
    }
}
