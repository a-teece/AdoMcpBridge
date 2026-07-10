using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class WorkItemFieldTypeCacheTests
{
    private readonly IAdoRestClient _ado = Substitute.For<IAdoRestClient>();

    private WorkItemFieldTypeCache CreateCache() => new(_ado);

    [Fact]
    public async Task Returns_field_names_for_html_and_plainText_types()
    {
        _ado.GetFieldRefNamesByTypeAsync(
                "myorg", Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(["System.Description", "Custom.Notes"]));

        var cache = CreateCache();
        var result = await cache.GetLongTextFieldRefNamesAsync("myorg");

        result.Should().Contain("System.Description");
        result.Should().Contain("Custom.Notes");
    }

    [Fact]
    public async Task Calls_ado_only_once_per_org_on_repeated_lookups()
    {
        _ado.GetFieldRefNamesByTypeAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(["System.Description"]));

        var cache = CreateCache();
        await cache.GetLongTextFieldRefNamesAsync("org1");
        await cache.GetLongTextFieldRefNamesAsync("org1");
        await cache.GetLongTextFieldRefNamesAsync("org1");

        await _ado.Received(1)
            .GetFieldRefNamesByTypeAsync("org1", Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Caches_independently_for_different_orgs()
    {
        _ado.GetFieldRefNamesByTypeAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        var cache = CreateCache();
        await cache.GetLongTextFieldRefNamesAsync("org-a");
        await cache.GetLongTextFieldRefNamesAsync("org-b");

        await _ado.Received(1)
            .GetFieldRefNamesByTypeAsync("org-a", Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>());
        await _ado.Received(1)
            .GetFieldRefNamesByTypeAsync("org-b", Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_empty_set_when_org_has_no_long_text_fields()
    {
        _ado.GetFieldRefNamesByTypeAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        var cache = CreateCache();
        var result = await cache.GetLongTextFieldRefNamesAsync("bare-org");

        result.Should().BeEmpty();
    }
}
