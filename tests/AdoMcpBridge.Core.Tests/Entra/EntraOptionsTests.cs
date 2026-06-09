using AdoMcpBridge.Core.Entra;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class EntraOptionsTests
{
    [Fact]
    public void Binds_from_configuration_section()
    {
        var dict = new Dictionary<string, string?>
        {
            ["AdoMcp:Entra:TenantId"] = "tid",
            ["AdoMcp:Entra:ClientId"] = "cid",
            ["AdoMcp:Entra:CertificateName"] = "ado-mcp-bridge",
            ["AdoMcp:Entra:Authority"] = "https://login.microsoftonline.com/tid/v2.0",
            ["AdoMcp:Entra:KeyVaultUri"] = "https://kv.example/",
            ["AdoMcp:Entra:Scopes:0"] = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation",
            ["AdoMcp:Entra:Scopes:1"] = "offline_access",
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddOptions<EntraOptions>().Bind(cfg.GetSection("AdoMcp:Entra"));
        var opts = services.BuildServiceProvider().GetRequiredService<IOptions<EntraOptions>>().Value;

        opts.TenantId.Should().Be("tid");
        opts.ClientId.Should().Be("cid");
        opts.CertificateName.Should().Be("ado-mcp-bridge");
        opts.Authority.Should().Be("https://login.microsoftonline.com/tid/v2.0");
        opts.KeyVaultUri.Should().Be("https://kv.example/");
        opts.Scopes.Should().Contain("offline_access");
    }
}
