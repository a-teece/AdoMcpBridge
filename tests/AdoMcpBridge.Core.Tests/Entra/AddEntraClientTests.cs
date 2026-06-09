using AdoMcpBridge.Core.Entra;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class AddEntraClientTests
{
    [Fact]
    public void Registers_IEntraTokenClient_and_ICertificateProvider()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdoMcp:Entra:TenantId"] = "tid",
            ["AdoMcp:Entra:ClientId"] = "cid",
            ["AdoMcp:Entra:CertificateName"] = "ado-mcp-bridge",
            ["AdoMcp:Entra:KeyVaultUri"] = "https://kv.example/",
            ["AdoMcp:Entra:Authority"] = "https://login.microsoftonline.com/tid/v2.0",
            ["AdoMcp:Entra:Scopes:0"] = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation",
            ["AdoMcp:Entra:Scopes:1"] = "offline_access",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new StubClock());
        services.AddEntraClient(cfg);

        using var sp = services.BuildServiceProvider();
        sp.GetService<IEntraTokenClient>().Should().BeOfType<EntraTokenClient>();
        sp.GetService<ICertificateProvider>().Should().BeOfType<CertificateProvider>();
    }

    private sealed class StubClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}
