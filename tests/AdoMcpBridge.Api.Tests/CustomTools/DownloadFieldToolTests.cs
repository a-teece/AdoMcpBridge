using System.Text.Json;
using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public sealed class DownloadFieldToolTests
{
    private DownloadFieldTool CreateTool() =>
        new(Substitute.For<IAdoRestClient>(), NullLogger<DownloadFieldTool>.Instance);

    [Fact]
    public void Advertises_itself_as_a_read_only_tool()
    {
        var tool = CreateTool();

        tool.Name.Should().Be("ado_bridge_download_field");
        JsonSerializer.Serialize(tool.Annotations).Should().Contain("readOnlyHint");
    }

    [Fact]
    public void Description_warns_content_enters_context_and_points_to_the_file_variant()
    {
        var description = CreateTool().Description;

        description.Should().Contain("context");
        description.Should().Contain("ado_bridge_download_field_as_file");
    }
}
