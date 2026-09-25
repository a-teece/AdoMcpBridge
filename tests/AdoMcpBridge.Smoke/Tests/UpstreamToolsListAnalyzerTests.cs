using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Smoke.Tests;

public class UpstreamToolsListAnalyzerTests
{
    // A rich, well-formed custom tool (like the bridge's own ado_bridge_* tools), an
    // action-dispatcher tool (the current upstream shape), and a bare-object tool (the
    // symptom we are hunting) — all in one plain-JSON tools/list result.
    private const string MixedBody = """
    {
      "jsonrpc": "2.0",
      "id": 1,
      "result": {
        "tools": [
          {
            "name": "ado_bridge_write_field_from_slot",
            "description": "Write operations: transfer a slot into a field.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "slotId": { "type": "string" },
                "fieldRefName": { "type": "string" }
              },
              "required": ["slotId", "fieldRefName"]
            }
          },
          {
            "name": "wit_work_item",
            "description": "Work item dispatcher.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "action": { "type": "string", "enum": ["get", "get_batch", "create"] },
                "id": { "type": "integer" }
              }
            }
          },
          {
            "name": "legacy_bare_tool",
            "description": "A tool with no declared parameters.",
            "inputSchema": { "type": "object" }
          }
        ]
      }
    }
    """;

    [Fact]
    public void Analyze_CountsEveryTool()
    {
        var report = UpstreamToolsListAnalyzer.Analyze(MixedBody);

        report.ToolCount.Should().Be(3);
    }

    [Fact]
    public void Analyze_RichSchema_CapturesPropertiesAndRequired()
    {
        var report = UpstreamToolsListAnalyzer.Analyze(MixedBody);

        var tool = report.Find("ado_bridge_write_field_from_slot");
        tool.Should().NotBeNull();
        tool!.HasProperties.Should().BeTrue();
        tool.IsBareObjectSchema.Should().BeFalse();
        tool.PropertyNames.Should().Contain(new[] { "slotId", "fieldRefName" });
        tool.Required.Should().Contain(new[] { "slotId", "fieldRefName" });
        tool.HasActionParam.Should().BeFalse();
    }

    [Fact]
    public void Analyze_ActionDispatcher_CapturesActionEnum()
    {
        var report = UpstreamToolsListAnalyzer.Analyze(MixedBody);

        var tool = report.Find("wit_work_item");
        tool.Should().NotBeNull();
        tool!.HasActionParam.Should().BeTrue();
        tool.ActionValues.Should().Equal("get", "get_batch", "create");
    }

    [Fact]
    public void Analyze_BareObjectSchema_IsFlagged()
    {
        var report = UpstreamToolsListAnalyzer.Analyze(MixedBody);

        var tool = report.Find("legacy_bare_tool");
        tool.Should().NotBeNull();
        tool!.IsBareObjectSchema.Should().BeTrue();
        tool.HasProperties.Should().BeFalse();
        report.BareObjectSchemaCount.Should().Be(1);
    }

    [Fact]
    public void ExtractResult_SseFramedBody_ParsesDataFrame()
    {
        const string sse =
            "event: message\n" +
            "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[{\"name\":\"only\"," +
            "\"inputSchema\":{\"type\":\"object\"}}]}}\n\n";

        var report = UpstreamToolsListAnalyzer.Analyze(sse);

        report.ToolCount.Should().Be(1);
        report.Find("only").Should().NotBeNull();
    }

    [Fact]
    public void ExtractResult_NoResult_Throws()
    {
        const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32603,\"message\":\"boom\"}}";

        var act = () => UpstreamToolsListAnalyzer.Analyze(body);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ToText_ListsToolNamesAndFlagsBareSchemas()
    {
        var report = UpstreamToolsListAnalyzer.Analyze(MixedBody);

        var text = report.ToText();

        text.Should().Contain("wit_work_item");
        text.Should().Contain("legacy_bare_tool");
        text.Should().Contain("bare-object schemas (no properties): 1");
    }
}
