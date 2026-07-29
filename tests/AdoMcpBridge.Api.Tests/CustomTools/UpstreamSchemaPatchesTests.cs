using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public sealed class UpstreamSchemaPatchesTests
{
    private const string UnpatchedToolsListResponse =
        """
        {"jsonrpc":"2.0","id":1,"result":{"tools":[
          {
            "name":"wit_work_item_write",
            "description":"Write operations on work items.",
            "inputSchema":{
              "type":"object",
              "properties":{
                "fields":{"description":"For create: a JSON array of field name/value pairs.","default":null},
                "updates":{"type":["array","null"],"default":null,"items":{"type":"object"}}
              }
            }
          },
          {
            "name":"some_other_tool",
            "description":"Untouched.",
            "inputSchema":{"type":"object","properties":{"fields":{"description":"unrelated"}}}
          }
        ]}}
        """;

    [Fact]
    public void Injects_missing_type_and_items_into_fields_in_plain_json_response()
    {
        var responseBytes = Encoding.UTF8.GetBytes(UnpatchedToolsListResponse);

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(responseBytes, []);

        using var doc = JsonDocument.Parse(patched);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var witTool = tools.EnumerateArray().First(t => t.GetProperty("name").GetString() == "wit_work_item_write");
        var fields = witTool.GetProperty("inputSchema").GetProperty("properties").GetProperty("fields");

        fields.GetProperty("type").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("array", "null");
        fields.GetProperty("items").GetProperty("type").GetString().Should().Be("object");
        fields.GetProperty("items").GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("name", "value");
        fields.GetProperty("items").GetProperty("additionalProperties").GetBoolean().Should().BeTrue();

        // Description text must survive verbatim.
        fields.GetProperty("description").GetString()
            .Should().Be("For create: a JSON array of field name/value pairs.");
    }

    [Fact]
    public void Leaves_other_tools_schemas_untouched()
    {
        var responseBytes = Encoding.UTF8.GetBytes(UnpatchedToolsListResponse);

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(responseBytes, []);

        using var doc = JsonDocument.Parse(patched);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var other = tools.EnumerateArray().First(t => t.GetProperty("name").GetString() == "some_other_tool");
        var fields = other.GetProperty("inputSchema").GetProperty("properties").GetProperty("fields");

        fields.TryGetProperty("type", out _).Should().BeFalse();
    }

    [Fact]
    public void Is_a_no_op_once_upstream_already_declares_a_type()
    {
        const string alreadyTyped =
            """
            {"jsonrpc":"2.0","id":1,"result":{"tools":[
              {
                "name":"wit_work_item_write",
                "description":"Write operations on work items.",
                "inputSchema":{
                  "type":"object",
                  "properties":{
                    "fields":{"type":["array","null"],"default":null,"items":{"type":"string"}}
                  }
                }
              }
            ]}}
            """;
        var responseBytes = Encoding.UTF8.GetBytes(alreadyTyped);

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(responseBytes, []);

        using var doc = JsonDocument.Parse(patched);
        var fields = doc.RootElement.GetProperty("result").GetProperty("tools")[0]
            .GetProperty("inputSchema").GetProperty("properties").GetProperty("fields");

        // Upstream's own (already-typed) items schema must survive untouched — proves
        // the patch backed off rather than overwriting a schema upstream already fixed.
        fields.GetProperty("items").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void Patches_fields_inside_an_sse_tools_list_response()
    {
        var sse = "event: message\ndata: " + UnpatchedToolsListResponse.ReplaceLineEndings("") + "\n\n";
        var responseBytes = Encoding.UTF8.GetBytes(sse);

        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(responseBytes, []);
        var patchedText = Encoding.UTF8.GetString(patched);

        var dataLine = patchedText.Split('\n').First(l => l.StartsWith("data:", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(dataLine["data:".Length..].Trim());
        var witTool = doc.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().First(t => t.GetProperty("name").GetString() == "wit_work_item_write");
        var fields = witTool.GetProperty("inputSchema").GetProperty("properties").GetProperty("fields");

        fields.GetProperty("type").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("array", "null");
    }
}
