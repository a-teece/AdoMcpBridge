using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitUpdateBatchToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();
    private readonly List<(int Id, string Ops)> _calls = [];

    public WitUpdateBatchToolTests()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ReturnsForAnyArgs(ci =>
            {
                var id = ci.ArgAt<int>(2);
                _calls.Add((id, JsonSerializer.Serialize(ci.ArgAt<IReadOnlyList<object>>(3))));
                return Parse($"{{\"id\":{id},\"rev\":{id + 100}}}");
            });
    }

    private WitUpdateBatchTool CreateTool() => new(_ado, NullLogger<WitUpdateBatchTool>.Instance);

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static object Update(int id, string state = "Active")
        => new { id, fields = new[] { new { name = "System.State", value = state } } };

    private Task<McpToolResult> Invoke(object updates)
        => CreateTool().InvokeAsync(Args(new { organization = "org", project = "proj", updates }), default);

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_NameWriteHintAndSchema()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("ado_bridge_wit_update_batch");
        JsonSerializer.SerializeToElement(tool.Annotations).GetProperty("readOnlyHint").GetBoolean()
            .Should().BeFalse();
        tool.Description.Should().Contain("50").And.Contain("ado_bridge_wit_update");

        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["organization", "project", "updates"]);
        schema.GetProperty("properties").GetProperty("updates").GetProperty("maxItems").GetInt32().Should().Be(50);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Batch_UpdatesEachItemSequentially_WithItsOwnOps()
    {
        var result = await Invoke(new object[]
        {
            Update(1, "Active"),
            new { id = 2, fields = new object[] { new { name = "System.Tags", op = "clear" }, new { name = "Microsoft.VSTS.Common.Priority", value = 1 } } },
        });

        result.IsError.Should().BeFalse();
        _calls.Should().Equal(
            (1, "[{\"op\":\"add\",\"path\":\"/fields/System.State\",\"value\":\"Active\"}]"),
            (2, "[{\"op\":\"remove\",\"path\":\"/fields/System.Tags\"}," +
                "{\"op\":\"add\",\"path\":\"/fields/Microsoft.VSTS.Common.Priority\",\"value\":\"1\"}]"));

        var results = Parse(result.Text).GetProperty("results");
        results.GetArrayLength().Should().Be(2);
        results[0].GetProperty("id").GetInt32().Should().Be(1);
        results[0].GetProperty("status").GetString().Should().Be("UPDATED");
        results[0].GetProperty("rev").GetInt32().Should().Be(101);
        results[0].TryGetProperty("error", out _).Should().BeFalse();
        results[1].GetProperty("rev").GetInt32().Should().Be(102);
    }

    // ── per-item failure isolation ───────────────────────────────────────────

    [Fact]
    public async Task Batch_OneFailure_DoesNotStopOthers()
    {
        _ado.UpdateWorkItemAsync("org", "proj", 2, Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AdoRestException(404, "TF401232: Work item 2 does not exist."));
        _ado.UpdateWorkItemAsync("org", "proj", 3, Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection reset"));

        var result = await Invoke(new[] { Update(1), Update(2), Update(3), Update(4) });

        result.IsError.Should().BeFalse();
        await _ado.ReceivedWithAnyArgs(4).UpdateWorkItemAsync(default!, default!, default, default!, default);

        var results = Parse(result.Text).GetProperty("results");
        results.EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).Should().Equal(1, 2, 3, 4);
        results.EnumerateArray().Select(r => r.GetProperty("status").GetString())
            .Should().Equal("UPDATED", "FAILED", "FAILED", "UPDATED");

        results[1].GetProperty("error").GetString().Should().Contain("HTTP 404").And.Contain("TF401232");
        results[1].TryGetProperty("rev", out _).Should().BeFalse();
        results[2].GetProperty("error").GetString().Should().Contain("connection reset");
        results[3].GetProperty("rev").GetInt32().Should().Be(104);
    }

    [Fact]
    public async Task Batch_AllFail_StillReportsEveryItem()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(403, "TF237111: no permission."));

        var result = await Invoke(new[] { Update(1), Update(2) });

        var results = Parse(result.Text).GetProperty("results");
        results.EnumerateArray().Select(r => r.GetProperty("status").GetString()).Should().Equal("FAILED", "FAILED");
    }

    [Fact]
    public async Task Batch_ResponseWithoutRev_OmitsRev()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ReturnsForAnyArgs(Parse("{\"id\":1}"));

        var result = await Invoke(new[] { Update(1) });

        var item = Parse(result.Text).GetProperty("results")[0];
        item.GetProperty("status").GetString().Should().Be("UPDATED");
        item.TryGetProperty("rev", out _).Should().BeFalse();
    }

    // ── size limits (1–50) ───────────────────────────────────────────────────

    [Fact]
    public async Task Batch_Exactly50_IsAccepted()
    {
        var result = await Invoke(Enumerable.Range(1, 50).Select(i => Update(i)).ToArray());

        result.IsError.Should().BeFalse();
        Parse(result.Text).GetProperty("results").GetArrayLength().Should().Be(50);
    }

    [Fact]
    public async Task Batch_Over50_Throws()
    {
        var act = () => Invoke(Enumerable.Range(1, 51).Select(i => Update(i)).ToArray());

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("updates").And.Contain("50");
        await NoWrite();
    }

    [Fact]
    public async Task Batch_Empty_Throws()
    {
        var act = () => Invoke(Array.Empty<object>());

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("updates");
    }

    [Fact]
    public async Task Batch_UpdatesMissingOrNotArray_Throws()
    {
        var missing = () => CreateTool().InvokeAsync(Args(new { organization = "org", project = "proj" }), default);
        var notArray = () => Invoke("nope");

        (await missing.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("updates");
        (await notArray.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("updates");
    }

    // ── validation happens before any write ──────────────────────────────────

    [Fact]
    public async Task Batch_ForbiddenFieldInLaterItem_RejectsWholeBatchBeforeAnyWrite()
    {
        var act = () => Invoke(new object[]
        {
            Update(1),
            new { id = 2, fields = new[] { new { name = "System.Description", value = "<p>x</p>" } } },
        });

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("updates[1]").And.Contain("ado_bridge_write_field_from_slot");
        await NoWrite();
    }

    [Fact]
    public async Task Batch_SystemParentInItem_RejectsWithLinkSteering()
    {
        var act = () => Invoke(new object[]
        {
            new { id = 1, fields = new[] { new { name = "System.Parent", value = "9" } } },
        });

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("updates[0]").And.Contain("ado_bridge_wit_link");
        await NoWrite();
    }

    [Fact]
    public async Task Batch_ItemMissingIdOrFields_Throws()
    {
        var noId = () => Invoke(new object[] { new { fields = new[] { new { name = "System.Title", value = "x" } } } });
        var noFields = () => Invoke(new object[] { new { id = 1 } });
        var notObject = () => Invoke(new object[] { 5 });

        (await noId.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("updates[0]").And.Contain("id");
        (await noFields.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("updates[0]").And.Contain("fields");
        (await notObject.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("updates[0]");
        await NoWrite();
    }

    private Task NoWrite()
        => _ado.DidNotReceiveWithAnyArgs().UpdateWorkItemAsync(default!, default!, default, default!, default);
}
