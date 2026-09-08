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

    // ── Native-tool steering in the basic tools' descriptions ────────────────

    private static JsonElement PatchAndFindTool(string responseJson, string toolName)
    {
        var patched = JsonRpcHelpers.InjectToolsIntoListResponse(Encoding.UTF8.GetBytes(responseJson), []);
        using var doc = JsonDocument.Parse(patched);
        return doc.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().First(t => t.GetProperty("name").GetString() == toolName).Clone();
    }

    [Fact]
    public void Steers_wit_work_item_write_towards_the_native_slot_tools()
    {
        var tool = PatchAndFindTool(UnpatchedToolsListResponse, "wit_work_item_write");

        var description = tool.GetProperty("description").GetString();
        description.Should().Contain("ado_bridge_create_upload_slot")
            .And.Contain("ado_bridge_write_field_from_slot")
            .And.Contain("System.Description")
            .And.Contain("Write operations on work items."); // upstream's own text survives
    }

    [Fact]
    public void Steers_wit_work_item_comment_write_towards_the_native_comment_tool()
    {
        const string withCommentWrite =
            """
            {"jsonrpc":"2.0","id":1,"result":{"tools":[
              {
                "name":"wit_work_item_comment_write",
                "description":"Add a comment to a work item.",
                "inputSchema":{"type":"object","properties":{"comment":{"type":"string"}}}
              }
            ]}}
            """;

        var tool = PatchAndFindTool(withCommentWrite, "wit_work_item_comment_write");

        var description = tool.GetProperty("description").GetString();
        description.Should().Contain("ado_bridge_add_comment")
            .And.Contain("Add a comment to a work item."); // upstream's own text survives
    }

    [Fact]
    public void Steers_wit_work_item_attachment_towards_the_native_download_tool()
    {
        const string withAttachment =
            """
            {"jsonrpc":"2.0","id":1,"result":{"tools":[
              {
                "name":"wit_work_item_attachment",
                "description":"Download a work item attachment by ID.",
                "inputSchema":{"type":"object","properties":{"id":{"type":"string"}}}
              }
            ]}}
            """;

        var tool = PatchAndFindTool(withAttachment, "wit_work_item_attachment");

        var description = tool.GetProperty("description").GetString();
        description.Should().Contain("ado_bridge_download_attachment")
            .And.Contain("Download a work item attachment by ID."); // upstream's own text survives
    }

    [Fact]
    public void Leaves_other_tools_descriptions_untouched()
    {
        var tool = PatchAndFindTool(UnpatchedToolsListResponse, "some_other_tool");

        tool.GetProperty("description").GetString().Should().Be("Untouched.");
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
