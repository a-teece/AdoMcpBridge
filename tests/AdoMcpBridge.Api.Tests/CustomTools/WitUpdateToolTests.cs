using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WitUpdateToolTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();
    private string? _sentOps;

    public WitUpdateToolTests()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ReturnsForAnyArgs(ci =>
            {
                _sentOps = JsonSerializer.Serialize(ci.ArgAt<IReadOnlyList<object>>(3));
                return Parse("{\"id\":42,\"rev\":7,\"fields\":{}}");
            });
    }

    private WitUpdateTool CreateTool() => new(_ado, NullLogger<WitUpdateTool>.Instance);

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private Task<McpToolResult> Invoke(object fields)
        => CreateTool().InvokeAsync(Args(new { organization = "org", project = "proj", id = 42, fields }), default);

    // ── metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_NameWriteHintAndSchema()
    {
        var tool = CreateTool();
        tool.Name.Should().Be("ado_bridge_wit_update");
        JsonSerializer.SerializeToElement(tool.Annotations).GetProperty("readOnlyHint").GetBoolean()
            .Should().BeFalse();
        tool.Description.Should().Contain("ado_bridge_write_field_from_slot").And.Contain("ado_bridge_wit_link");

        var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(["organization", "project", "id", "fields"]);
        var item = schema.GetProperty("properties").GetProperty("fields").GetProperty("items");
        item.GetProperty("properties").GetProperty("op").GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("set", "clear");
    }

    // ── op mapping ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_SetAndClear_BuildReplaceAndRemoveOps()
    {
        var result = await Invoke(new object[]
        {
            new { name = "System.State", value = "Active" },
            new { name = "System.Tags", op = "clear" },
        });

        result.IsError.Should().BeFalse();
        _sentOps.Should().Be(
            "[{\"op\":\"add\",\"path\":\"/fields/System.State\",\"value\":\"Active\"}," +
            "{\"op\":\"remove\",\"path\":\"/fields/System.Tags\"}]");
        await _ado.Received(1).UpdateWorkItemAsync("org", "proj", 42, Arg.Any<IReadOnlyList<object>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ExplicitSetOp_BuildsAddOp()
    {
        await Invoke(new[] { new { name = "System.Title", value = "New title", op = "set" } });

        _sentOps.Should().Be("[{\"op\":\"add\",\"path\":\"/fields/System.Title\",\"value\":\"New title\"}]");
    }

    [Fact]
    public async Task Update_ClearWithValue_IgnoresValue()
    {
        await Invoke(new[] { new { name = "System.Tags", value = "ignored", op = "clear" } });

        _sentOps.Should().Be("[{\"op\":\"remove\",\"path\":\"/fields/System.Tags\"}]");
    }

    [Fact]
    public async Task Update_FieldNameIsTrimmed()
    {
        await Invoke(new[] { new { name = "  System.Title ", value = "t" } });

        _sentOps.Should().Be("[{\"op\":\"add\",\"path\":\"/fields/System.Title\",\"value\":\"t\"}]");
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("SET")]
    [InlineData("")]
    public async Task Update_UnknownOp_Throws(string op)
    {
        var act = () => Invoke(new[] { new { name = "System.Title", value = "t", op } });

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("op").And.Contain("set").And.Contain("clear");
        await NoWrite();
    }

    [Fact]
    public async Task Update_NonStringOp_Throws()
    {
        var act = () => Invoke(new[] { new { name = "System.Title", value = "t", op = (object)1 } });

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("op");
    }

    // ── value stringification ────────────────────────────────────────────────

    [Fact]
    public async Task Update_NumericValue_IsStringified()
    {
        await Invoke(new object[]
        {
            new { name = "Microsoft.VSTS.Common.Priority", value = 2 },
            new { name = "Microsoft.VSTS.Scheduling.RemainingWork", value = 1.5 },
        });

        _sentOps.Should().Be(
            "[{\"op\":\"add\",\"path\":\"/fields/Microsoft.VSTS.Common.Priority\",\"value\":\"2\"}," +
            "{\"op\":\"add\",\"path\":\"/fields/Microsoft.VSTS.Scheduling.RemainingWork\",\"value\":\"1.5\"}]");
    }

    [Fact]
    public async Task Update_BoolValue_IsStringified()
    {
        await Invoke(new object[]
        {
            new { name = "Custom.Flag", value = true },
            new { name = "Custom.Other", value = false },
        });

        _sentOps.Should().Be(
            "[{\"op\":\"add\",\"path\":\"/fields/Custom.Flag\",\"value\":\"true\"}," +
            "{\"op\":\"add\",\"path\":\"/fields/Custom.Other\",\"value\":\"false\"}]");
    }

    [Fact]
    public async Task Update_StringValue_IsSentVerbatim_IncludingEmpty()
    {
        await Invoke(new[] { new { name = "System.Title", value = "a \"quoted\" <b>title</b>" }, new { name = "Custom.X", value = "" } });

        var ops = Parse(_sentOps!);
        ops[0].GetProperty("value").GetString().Should().Be("a \"quoted\" <b>title</b>");
        ops[1].GetProperty("value").GetString().Should().Be("");
    }

    [Fact]
    public async Task Update_SetWithoutValue_Throws()
    {
        var act = () => Invoke(new[] { new { name = "System.Title" } });

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("value").And.Contain("System.Title");
        await NoWrite();
    }

    [Fact]
    public async Task Update_SetWithNullValue_Throws()
    {
        var act = () => Invoke(new[] { new { name = "System.Title", value = (string?)null } });

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("value");
    }

    [Fact]
    public async Task Update_ObjectValue_Throws()
    {
        var act = () => Invoke(new[] { new { name = "System.Title", value = (object)new { a = 1 } } });

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("string, number or boolean");
        await NoWrite();
    }

    // ── argument validation ──────────────────────────────────────────────────

    [Fact]
    public async Task Update_EmptyFields_Throws()
    {
        var act = () => Invoke(Array.Empty<object>());

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("fields");
        await NoWrite();
    }

    [Fact]
    public async Task Update_MissingFields_Throws()
    {
        var act = () => CreateTool().InvokeAsync(Args(new { organization = "org", project = "proj", id = 42 }), default);

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("fields");
    }

    [Fact]
    public async Task Update_FieldsNotArray_Throws()
    {
        var act = () => Invoke("System.Title=x");

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("fields");
    }

    [Fact]
    public async Task Update_FieldEntryNotObject_Throws()
    {
        var act = () => Invoke(new[] { "System.Title" });

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("fields");
    }

    [Fact]
    public async Task Update_MissingName_Throws()
    {
        var act = () => Invoke(new[] { new { value = "x" } });

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("name");
    }

    [Fact]
    public async Task Update_NameContainingSlash_Throws()
    {
        var act = () => Invoke(new[] { new { name = "System.Title/../relations/-", value = "x" } });

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("'/'");
        await NoWrite();
    }

    [Fact]
    public async Task Update_MissingId_Throws()
    {
        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", fields = new[] { new { name = "System.Title", value = "x" } } }),
            default);

        (await act.Should().ThrowAsync<CallerArgumentException>()).Which.Message.Should().Contain("id");
    }

    // ── forbidden fields (Review Focus 2) ────────────────────────────────────

    [Theory]
    [InlineData("System.Description", "set")]
    [InlineData("Microsoft.VSTS.TCM.ReproSteps", "clear")]
    [InlineData("microsoft.vsts.common.acceptancecriteria", "set")]
    public async Task Update_LongTextField_IsRejectedWithSteering_AndNothingWritten(string name, string op)
    {
        var act = () => Invoke(new object[]
        {
            new { name = "System.Title", value = "fine" },
            new { name, value = "<p>body</p>", op },
        });

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("ado_bridge_write_field_from_slot");
        await NoWrite();
    }

    [Theory]
    [InlineData("set")]
    [InlineData("clear")]
    public async Task Update_SystemParent_IsRejectedWithSteering_AndNothingWritten(string op)
    {
        var act = () => Invoke(new[] { new { name = "System.Parent", value = "123", op } });

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("ado_bridge_wit_link");
        await NoWrite();
    }

    [Theory]
    [InlineData("System.History", "set")]
    [InlineData("System.History", "clear")]
    [InlineData(" system.history ", "set")]
    public async Task Update_SystemHistory_IsRejectedWithAddCommentSteering_AndNothingWritten(string name, string op)
    {
        var act = () => Invoke(new object[]
        {
            new { name = "System.State", value = "Active" },
            new { name, value = "a comment", op },
        });

        (await act.Should().ThrowAsync<CallerArgumentException>())
            .Which.Message.Should().Contain("ado_bridge_add_comment");
        await NoWrite();
    }

    // ── result shape ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ReturnsUpdatedStatus_IdRevAndChangedFields()
    {
        var result = await Invoke(new object[]
        {
            new { name = "System.State", value = "Active" },
            new { name = "System.Tags", op = "clear" },
        });

        var root = Parse(result.Text);
        root.GetProperty("status").GetString().Should().Be("UPDATED");
        root.GetProperty("id").GetInt32().Should().Be(42);
        root.GetProperty("rev").GetInt32().Should().Be(7);
        root.GetProperty("changedFields").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("System.State", "System.Tags");
    }

    [Fact]
    public async Task Update_ResponseWithoutRev_ReturnsNullRev()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ReturnsForAnyArgs(Parse("{\"id\":42}"));

        var result = await Invoke(new[] { new { name = "System.Title", value = "t" } });

        Parse(result.Text).GetProperty("rev").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── ADO failures surface verbatim ────────────────────────────────────────

    [Fact]
    public async Task Update_AdoRestException_ReturnsIsError()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new AdoRestException(400,
                "TF401320: Rule Error for field State. Error code: Required, InvalidListValue."));

        var result = await Invoke(new[] { new { name = "System.State", value = "Bogus" } });

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("HTTP 400").And.Contain("TF401320");
    }

    [Fact]
    public async Task Update_HttpRequestException_ReturnsIsError()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("connection reset"));

        var result = await Invoke(new[] { new { name = "System.State", value = "Active" } });

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("connection reset");
    }

    [Fact]
    public async Task Update_Timeout_ReturnsIsError_SayingWriteMayHaveBeenApplied()
    {
        // HttpClient's own timeout surfaces as TaskCanceledException with the caller's token
        // NOT cancelled.
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var result = await Invoke(new[] { new { name = "System.State", value = "Active" } });

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("timed out").And.Contain("MAY have been applied")
            .And.Contain("re-read").And.Contain("before retrying");
    }

    [Fact]
    public async Task Update_NonJsonSuccessBody_ReturnsIsError_SayingWriteMayHaveBeenApplied()
    {
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new JsonException("'<' is an invalid start of a value."));

        var result = await Invoke(new[] { new { name = "System.State", value = "Active" } });

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("MAY have been applied").And.Contain("re-read");
    }

    [Fact]
    public async Task Update_RealCallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _ado.UpdateWorkItemAsync(default!, default!, default, default!, default)
            .ThrowsAsyncForAnyArgs(new OperationCanceledException(cts.Token));

        var act = () => CreateTool().InvokeAsync(
            Args(new { organization = "org", project = "proj", id = 42, fields = new[] { new { name = "System.Title", value = "t" } } }),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private Task NoWrite()
        => _ado.DidNotReceiveWithAnyArgs().UpdateWorkItemAsync(default!, default!, default, default!, default);
}
