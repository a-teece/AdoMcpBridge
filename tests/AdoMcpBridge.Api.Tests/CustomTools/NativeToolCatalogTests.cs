using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Api.Tests.CustomTools;

/// <summary>
/// Guards the whole native tool catalog as resolved from the real DI container, so every
/// new <c>ado_bridge_*</c> tool is checked for a unique, prefixed name and an object schema.
/// </summary>
public sealed class NativeToolCatalogTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _factory;

    public NativeToolCatalogTests(BridgeApiFactory factory) => _factory = factory;

    [Fact]
    public void AllNativeTools_HaveUniqueAdoBridgePrefixedNames_AndObjectSchemas()
    {
        var tools = _factory.Services.GetServices<ICustomMcpTool>().ToList();
        var names = tools.Select(t => t.Name).ToList();

        names.Should().NotBeEmpty();
        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n.StartsWith("ado_bridge_", StringComparison.Ordinal));
        names.Should().Contain("ado_bridge_wit_search");
        names.Should().Contain("ado_bridge_wit_list");
        names.Should().Contain("ado_bridge_wit_list_queries");
        names.Should().Contain("ado_bridge_wit_run_query");
        names.Should().Contain("ado_bridge_wit_update");
        names.Should().Contain("ado_bridge_wit_update_batch");

        foreach (var tool in tools)
        {
            JsonSerializer.SerializeToElement(tool.InputSchema).GetProperty("type").GetString()
                .Should().Be("object", tool.Name);
        }
    }
}
