using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

/// <summary>
/// Unit tests for the initialize-capability patch and the tools/list_changed frame
/// builder introduced for the self-healing refresh feature.
/// </summary>
public sealed class JsonRpcHelpersRefreshTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void PatchInitialize_sets_listChanged_true_on_plain_json()
    {
        var patched = JsonRpcHelpers.PatchInitializeListChanged(
            Bytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"capabilities\":{\"tools\":{}}}}"));

        using var doc = JsonDocument.Parse(patched);
        doc.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void PatchInitialize_creates_capabilities_when_absent()
    {
        var patched = JsonRpcHelpers.PatchInitializeListChanged(
            Bytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"x\"}}"));

        using var doc = JsonDocument.Parse(patched);
        doc.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void PatchInitialize_creates_tools_when_capabilities_present_without_tools()
    {
        var patched = JsonRpcHelpers.PatchInitializeListChanged(
            Bytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"capabilities\":{\"logging\":{}}}}"));

        using var doc = JsonDocument.Parse(patched);
        var caps = doc.RootElement.GetProperty("result").GetProperty("capabilities");
        caps.GetProperty("logging").ValueKind.Should().Be(JsonValueKind.Object);
        caps.GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void PatchInitialize_returns_original_when_no_result_object()
    {
        var original = "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-1,\"message\":\"boom\"}}";

        var patched = JsonRpcHelpers.PatchInitializeListChanged(Bytes(original));

        Encoding.UTF8.GetString(patched).Should().Be(original);
    }

    [Fact]
    public void PatchInitialize_returns_original_when_body_is_not_json()
    {
        var patched = JsonRpcHelpers.PatchInitializeListChanged(Bytes("this is not json at all"));

        Encoding.UTF8.GetString(patched).Should().Be("this is not json at all");
    }

    [Fact]
    public void PatchInitialize_patches_sse_body()
    {
        var sse = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"capabilities\":{}}}\n\n";

        var patched = JsonRpcHelpers.PatchInitializeListChanged(Bytes(sse));
        var text = Encoding.UTF8.GetString(patched);

        var dataLine = text.Split('\n').First(l => l.StartsWith("data:", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(dataLine["data:".Length..].Trim());
        doc.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void PatchInitialize_leaves_sse_data_lines_without_a_result_untouched()
    {
        // A [DONE] sentinel, an empty data line, and a resultless data frame must all pass
        // through the SSE patcher unchanged.
        var sse = "event: message\ndata: [DONE]\ndata:\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"ping\"}\n\n";

        var patched = JsonRpcHelpers.PatchInitializeListChanged(Bytes(sse));
        var text = Encoding.UTF8.GetString(patched);

        text.Should().Contain("[DONE]");
        text.Should().Contain("\"method\":\"ping\"");
        text.Should().NotContain("listChanged");
    }

    [Fact]
    public void TryPrependFrame_prepends_notification_to_sse_body()
    {
        var sse = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n\n";

        var injected = JsonRpcHelpers.TryPrependToolsListChangedFrame(Bytes(sse), out var result);

        injected.Should().BeTrue();
        var text = Encoding.UTF8.GetString(result);
        text.Should().StartWith("event: message\ndata: " + JsonRpcHelpers.ToolsListChangedNotification);
        text.IndexOf("list_changed", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("\"result\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TryPrependFrame_leaves_plain_json_untouched()
    {
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}";

        var injected = JsonRpcHelpers.TryPrependToolsListChangedFrame(Bytes(json), out var result);

        injected.Should().BeFalse();
        Encoding.UTF8.GetString(result).Should().Be(json);
    }

    private sealed class AnnotatedTool : ICustomMcpTool
    {
        public string Name => "annotated_tool";
        public string Description => "d";
        public object InputSchema => new { type = "object" };
        public object? Annotations => new { readOnlyHint = true };
        public Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
            => Task.FromResult(new McpToolResult("ok"));
    }

    [Fact]
    public void InjectTools_adds_custom_tool_with_annotations_into_plain_json_list()
    {
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[]}}";

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(Bytes(body), [new AnnotatedTool()]);

        using var doc = JsonDocument.Parse(patched);
        var tool = doc.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Single();
        tool.GetProperty("name").GetString().Should().Be("annotated_tool");
        tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void InjectTools_returns_body_unchanged_when_there_is_no_tools_result()
    {
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[]}}";

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(Bytes(body), [new AnnotatedTool()]);

        Encoding.UTF8.GetString(patched).Should().Be(body);
    }

    [Fact]
    public void InjectTools_returns_body_unchanged_when_not_parseable()
    {
        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(Bytes("not json"), [new AnnotatedTool()]);

        Encoding.UTF8.GetString(patched).Should().Be("not json");
    }

    [Fact]
    public void InjectTools_preserves_sse_done_and_empty_data_lines()
    {
        var sse = "event: message\ndata: [DONE]\ndata:\n\n";

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(Bytes(sse), [new AnnotatedTool()]);
        var text = Encoding.UTF8.GetString(patched);

        text.Should().Contain("[DONE]");
        text.Should().NotContain("annotated_tool");
    }

    [Fact]
    public void PatchInitialize_patches_sse_body_that_starts_with_data_prefix()
    {
        // No leading `event:` line — exercises the `data:`-prefix arm of SSE detection.
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"capabilities\":{}}}\n\n";

        var patched = JsonRpcHelpers.PatchInitializeListChanged(Bytes(sse));
        var text = Encoding.UTF8.GetString(patched);

        var dataLine = text.Split('\n').First(l => l.StartsWith("data:", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(dataLine["data:".Length..].Trim());
        doc.RootElement.GetProperty("result").GetProperty("capabilities")
            .GetProperty("tools").GetProperty("listChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void PatchInitialize_returns_original_when_json_is_the_null_literal()
    {
        var patched = JsonRpcHelpers.PatchInitializeListChanged(Bytes("null"));

        Encoding.UTF8.GetString(patched).Should().Be("null");
    }

    [Fact]
    public void PatchInitialize_sse_skips_a_null_literal_data_line()
    {
        var sse = "event: message\ndata: null\n\n";

        var patched = JsonRpcHelpers.PatchInitializeListChanged(Bytes(sse));

        Encoding.UTF8.GetString(patched).Should().NotContain("listChanged");
    }

    [Fact]
    public void TryPrependFrame_detects_sse_body_that_starts_with_data_prefix()
    {
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\n\n";

        var injected = JsonRpcHelpers.TryPrependToolsListChangedFrame(Bytes(sse), out var result);

        injected.Should().BeTrue();
        Encoding.UTF8.GetString(result)
            .Should().StartWith("event: message\ndata: " + JsonRpcHelpers.ToolsListChangedNotification);
    }

    [Fact]
    public void InjectTools_detects_sse_body_that_starts_with_data_prefix()
    {
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[]}}\n\n";

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(Bytes(sse), [new AnnotatedTool()]);

        Encoding.UTF8.GetString(patched).Should().Contain("annotated_tool");
    }

    [Fact]
    public void InjectTools_leaves_sse_data_lines_without_a_tools_result_untouched()
    {
        // First data line: valid JSON with a result but no tools array.
        // Second data line: valid JSON with no result at all.
        var sse =
            "event: message\ndata: {\"jsonrpc\":\"2.0\",\"result\":{\"content\":[]}}\n" +
            "data: {\"jsonrpc\":\"2.0\",\"method\":\"ping\"}\n\n";

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(Bytes(sse), [new AnnotatedTool()]);
        var text = Encoding.UTF8.GetString(patched);

        text.Should().Contain("\"content\"");
        text.Should().Contain("\"method\":\"ping\"");
        text.Should().NotContain("annotated_tool");
    }
}
