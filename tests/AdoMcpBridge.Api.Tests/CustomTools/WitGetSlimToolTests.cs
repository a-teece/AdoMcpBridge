using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitGetSlimToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();
    private readonly IWorkItemFieldTypeCache _cache = Substitute.For<IWorkItemFieldTypeCache>();

    private WitGetSlimTool CreateTool() =>
        new(_ado, _cache, NullLogger<WitGetSlimTool>.Instance);

    // ── BuildSlimJson (pure, no IO) ──────────────────────────────────────────

    [Fact]
    public void BuildSlimJson_stubs_non_empty_html_field_with_charCount_and_note()
    {
        var wi = MakeWorkItem(1, new Dictionary<string, object?> { ["System.Description"] = "<p>Hello</p>" });
        var longText = new HashSet<string>(["System.Description"]);

        var json = WitGetSlimTool.BuildSlimJson(wi!.Value, longText);

        var doc = JsonDocument.Parse(json).RootElement;
        var desc = doc.GetProperty("fields").GetProperty("System.Description");
        desc.GetProperty("charCount").GetInt32().Should().Be("<p>Hello</p>".Length);
        desc.GetProperty("note").GetString().Should().Contain("ado_bridge_download_field");
    }

    [Fact]
    public void BuildSlimJson_passes_through_non_long_text_fields_unchanged()
    {
        var wi = MakeWorkItem(2, new Dictionary<string, object?> { ["System.Title"] = "My bug", ["System.State"] = "Active" });

        var json = WitGetSlimTool.BuildSlimJson(wi!.Value, new HashSet<string>());

        var fields = JsonDocument.Parse(json).RootElement.GetProperty("fields");
        fields.GetProperty("System.Title").GetString().Should().Be("My bug");
        fields.GetProperty("System.State").GetString().Should().Be("Active");
    }

    [Fact]
    public void BuildSlimJson_does_not_stub_null_long_text_field()
    {
        var wi = MakeWorkItem(3, new Dictionary<string, object?> { ["System.Description"] = null });
        var longText = new HashSet<string>(["System.Description"]);

        var json = WitGetSlimTool.BuildSlimJson(wi!.Value, longText);

        var desc = JsonDocument.Parse(json).RootElement.GetProperty("fields").GetProperty("System.Description");
        desc.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void BuildSlimJson_does_not_stub_empty_string_long_text_field()
    {
        var wi = MakeWorkItem(4, new Dictionary<string, object?> { ["System.Description"] = "" });
        var longText = new HashSet<string>(["System.Description"]);

        var json = WitGetSlimTool.BuildSlimJson(wi!.Value, longText);

        var desc = JsonDocument.Parse(json).RootElement.GetProperty("fields").GetProperty("System.Description");
        desc.GetString().Should().BeEmpty();
    }

    [Fact]
    public void BuildSlimJson_preserves_top_level_id_rev_url()
    {
        var wi = MakeWorkItem(42, new Dictionary<string, object?>());

        var json = WitGetSlimTool.BuildSlimJson(wi!.Value, new HashSet<string>());

        var doc = JsonDocument.Parse(json).RootElement;
        doc.GetProperty("id").GetInt32().Should().Be(42);
        doc.GetProperty("rev").GetInt32().Should().Be(1);
        doc.GetProperty("url").GetString().Should().Contain("42");
    }

    [Fact]
    public void BuildSlimJson_stubs_multiple_long_text_fields_independently()
    {
        var wi = MakeWorkItem(5, new Dictionary<string, object?>
        {
            ["System.Description"] = "<div>Description content</div>",
            ["Microsoft.VSTS.TCM.ReproSteps"] = "<ol><li>Step 1</li></ol>",
            ["System.Title"] = "My title",
        });
        var longText = new HashSet<string>(["System.Description", "Microsoft.VSTS.TCM.ReproSteps"]);

        var json = WitGetSlimTool.BuildSlimJson(wi!.Value, longText);

        var fields = JsonDocument.Parse(json).RootElement.GetProperty("fields");
        fields.GetProperty("System.Description").TryGetProperty("charCount", out _).Should().BeTrue();
        fields.GetProperty("Microsoft.VSTS.TCM.ReproSteps").TryGetProperty("charCount", out _).Should().BeTrue();
        fields.GetProperty("System.Title").GetString().Should().Be("My title");
    }

    // ── InvokeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_returns_slim_json_for_existing_work_item()
    {
        var wi = MakeWorkItem(10, new Dictionary<string, object?>
        {
            ["System.Title"] = "Bug",
            ["System.Description"] = "<p>Long description</p>",
        });
        _ado.GetWorkItemAsync("org", "proj", 10, Arg.Any<CancellationToken>()).Returns(wi);
        _cache.GetLongTextFieldRefNamesAsync("org", Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(["System.Description"]));

        var result = await CreateTool().InvokeAsync(MakeArgs("org", "proj", 10), default);

        result.IsError.Should().BeFalse();
        var doc = JsonDocument.Parse(result.Text).RootElement;
        doc.GetProperty("fields").GetProperty("System.Title").GetString().Should().Be("Bug");
        doc.GetProperty("fields").GetProperty("System.Description")
            .TryGetProperty("charCount", out _).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_returns_error_when_work_item_not_found()
    {
        _ado.GetWorkItemAsync("org", "proj", 999, Arg.Any<CancellationToken>())
            .Returns((JsonElement?)null);
        _cache.GetLongTextFieldRefNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        var result = await CreateTool().InvokeAsync(MakeArgs("org", "proj", 999), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("not found");
    }

    [Fact]
    public async Task InvokeAsync_returns_error_on_ado_http_failure()
    {
        _ado.GetWorkItemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<JsonElement?>(_ => throw new HttpRequestException("401 Unauthorized"));
        _cache.GetLongTextFieldRefNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        var result = await CreateTool().InvokeAsync(MakeArgs("org", "proj", 1), default);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("ADO request failed");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static JsonElement? MakeWorkItem(int id, Dictionary<string, object?> fields)
    {
        var json = JsonSerializer.Serialize(new
        {
            id,
            rev = 1,
            url = $"https://dev.azure.com/test/proj/_apis/wit/workitems/{id}",
            fields,
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement MakeArgs(string org, string project, int id)
    {
        using var doc = JsonDocument.Parse(
            JsonSerializer.Serialize(new { organization = org, project, id }));
        return doc.RootElement.Clone();
    }
}
