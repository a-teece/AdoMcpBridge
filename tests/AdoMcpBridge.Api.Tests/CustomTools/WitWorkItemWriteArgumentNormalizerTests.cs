using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public sealed class WitWorkItemWriteArgumentNormalizerTests
{
    private static JsonDocument BuildRequest(string argumentsJson) =>
        JsonDocument.Parse(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"," +
            "\"params\":{\"name\":\"wit_work_item_write\",\"arguments\":" + argumentsJson + "}}");

    private static JsonElement ExtractArgument(byte[] rewritten, string name)
    {
        using var doc = JsonDocument.Parse(rewritten);
        return doc.RootElement.GetProperty("params").GetProperty("arguments").GetProperty(name).Clone();
    }

    [Fact]
    public void Already_an_array_of_strings_passes_through_unmodified()
    {
        using var doc = BuildRequest(
            "{\"action\":\"create\",\"fields\":[{\"name\":\"System.Title\",\"value\":\"x\"}]}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().BeNull();
    }

    [Fact]
    public void Single_json_encoded_string_is_parsed_into_an_array()
    {
        var arrayText = JsonSerializer.Serialize(new[] { new { name = "System.Title", value = "x" } });
        using var doc = BuildRequest(
            "{\"action\":\"create\",\"fields\":" + JsonSerializer.Serialize(arrayText) + "}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().NotBeNull();
        var fields = ExtractArgument(result!, "fields");
        fields.ValueKind.Should().Be(JsonValueKind.Array);
        fields[0].GetProperty("name").GetString().Should().Be("System.Title");
        fields[0].GetProperty("value").GetString().Should().Be("x");
    }

    [Fact]
    public void Double_json_encoded_string_is_unwrapped_into_an_array()
    {
        var arrayText = JsonSerializer.Serialize(new[] { new { name = "System.Title", value = "x" } });
        var doubleEncoded = JsonSerializer.Serialize(arrayText);
        using var doc = BuildRequest(
            "{\"action\":\"create\",\"fields\":" + JsonSerializer.Serialize(doubleEncoded) + "}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().NotBeNull();
        var fields = ExtractArgument(result!, "fields");
        fields.ValueKind.Should().Be(JsonValueKind.Array);
        fields[0].GetProperty("name").GetString().Should().Be("System.Title");
    }

    [Fact]
    public void Map_shape_object_is_converted_to_name_value_pairs()
    {
        using var doc = BuildRequest(
            "{\"action\":\"create\",\"fields\":{\"System.Title\":\"x\",\"System.Tags\":\"urgent\"}}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().NotBeNull();
        var fields = ExtractArgument(result!, "fields");
        fields.ValueKind.Should().Be(JsonValueKind.Array);
        fields.GetArrayLength().Should().Be(2);
        fields[0].GetProperty("name").GetString().Should().Be("System.Title");
        fields[0].GetProperty("value").GetString().Should().Be("x");
        fields[1].GetProperty("name").GetString().Should().Be("System.Tags");
        fields[1].GetProperty("value").GetString().Should().Be("urgent");
    }

    [Fact]
    public void Single_name_value_object_is_wrapped_in_an_array()
    {
        using var doc = BuildRequest(
            "{\"action\":\"create\",\"fields\":{\"name\":\"System.Title\",\"value\":\"x\"}}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().NotBeNull();
        var fields = ExtractArgument(result!, "fields");
        fields.ValueKind.Should().Be(JsonValueKind.Array);
        fields.GetArrayLength().Should().Be(1);
        fields[0].GetProperty("name").GetString().Should().Be("System.Title");
        fields[0].GetProperty("value").GetString().Should().Be("x");
    }

    [Fact]
    public void Bare_number_value_is_stringified()
    {
        using var doc = BuildRequest(
            "{\"action\":\"create\",\"fields\":[{\"name\":\"Microsoft.VSTS.Common.StackRank\",\"value\":100}]}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().NotBeNull();
        var fields = ExtractArgument(result!, "fields");
        fields[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
        fields[0].GetProperty("value").GetString().Should().Be("100");
    }

    [Fact]
    public void Bare_boolean_value_is_stringified()
    {
        using var doc = BuildRequest(
            "{\"action\":\"create\",\"fields\":[{\"name\":\"Custom.IsBlocked\",\"value\":true}]}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().NotBeNull();
        var fields = ExtractArgument(result!, "fields");
        fields[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
        fields[0].GetProperty("value").GetString().Should().Be("true");
    }

    [Fact]
    public void Invalid_json_string_throws_naming_the_parameter()
    {
        using var doc = BuildRequest("{\"action\":\"create\",\"fields\":\"not valid json{\"}");

        var act = () => WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        act.Should().Throw<WitWorkItemWriteArgumentException>().WithMessage("*fields*");
    }

    [Fact]
    public void Non_array_non_object_value_throws_naming_the_parameter()
    {
        using var doc = BuildRequest("{\"action\":\"create\",\"fields\":42}");

        var act = () => WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        act.Should().Throw<WitWorkItemWriteArgumentException>().WithMessage("*fields*");
    }

    [Fact]
    public void Null_fields_is_left_untouched()
    {
        using var doc = BuildRequest("{\"action\":\"update\",\"fields\":null}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().BeNull();
    }

    [Fact]
    public void Normalizes_updates_items_and_batchUpdates_the_same_way_as_fields()
    {
        using var doc = BuildRequest(
            "{\"action\":\"update_batch\",\"batchUpdates\":" +
            "\"[{\\\"id\\\":1,\\\"op\\\":\\\"Replace\\\",\\\"path\\\":\\\"/fields/System.State\\\",\\\"value\\\":\\\"Active\\\"}]\"}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().NotBeNull();
        var batchUpdates = ExtractArgument(result!, "batchUpdates");
        batchUpdates.ValueKind.Should().Be(JsonValueKind.Array);
        batchUpdates[0].GetProperty("path").GetString().Should().Be("/fields/System.State");
    }

    [Fact]
    public void Arguments_with_no_recognised_array_parameters_are_untouched()
    {
        using var doc = BuildRequest("{\"action\":\"update\",\"id\":123}");

        var result = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(doc.RootElement);

        result.Should().BeNull();
    }
}
