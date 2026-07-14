using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.BlobStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using WireMock.Server;

namespace AdoMcpBridge.Api.Tests.Proxy;

public sealed class ProxyTestFixture : WebApplicationFactory<Program>, IAsyncDisposable
{
    public WireMockServer Upstream { get; } = WireMockServer.Start();
    public IEntraTokenClient EntraClient { get; } = Substitute.For<IEntraTokenClient>();
    public IKeyVaultEncryptor Encryptor { get; } = Substitute.For<IKeyVaultEncryptor>();
    public ITokenStore TokenStore { get; } = Substitute.For<ITokenStore>();
    public IClock Clock { get; } = new TestClock { UtcNow = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero) };
    public IBlobSlotStore BlobSlotStore { get; } = Substitute.For<IBlobSlotStore>();
    public IAdoRestClient AdoRestClient { get; } = Substitute.For<IAdoRestClient>();

    public sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting (not ConfigureAppConfiguration) because Program.cs
        // reads this eagerly at registration time; test config sources
        // are appended too late for eager reads under minimal hosting.
        builder.UseSetting("AdoMcp:Database:ConnectionString", "Server=localhost;Database=test;");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Routes:mcp:ClusterId"] = "ado-mcp",
                ["ReverseProxy:Routes:mcp:Match:Path"] = "/mcp/{**catch-all}",
                ["ReverseProxy:Routes:mcp:Transforms:0:PathPattern"] = "/{**catch-all}",
                ["ReverseProxy:Clusters:ado-mcp:Destinations:primary:Address"] = Upstream.Urls[0],
                ["AdoMcp:Issuer"] = "https://localhost",
                ["AdoMcp:BlobStorage:AccountUri"] = "https://stadomcptest.blob.core.windows.net/",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEntraTokenClient>();
            services.RemoveAll<IKeyVaultEncryptor>();
            services.RemoveAll<ITokenStore>();
            services.RemoveAll<IClock>();
            services.RemoveAll<AdoMcpBridge.Core.OAuth.IAuthorizationSessionCache>();
            services.RemoveAll<IBlobSlotStore>();
            services.RemoveAll<IAdoRestClient>();
            services.AddSingleton(EntraClient);
            services.AddSingleton(Encryptor);
            services.AddSingleton(TokenStore);
            services.AddSingleton(Clock);
            services.AddSingleton<AdoMcpBridge.Core.OAuth.IAuthorizationSessionCache>(
                new AdoMcpBridge.Core.OAuth.InMemoryAuthorizationSessionCache(Clock));
            services.AddSingleton(BlobSlotStore);
            services.AddSingleton(AdoRestClient);
        });
    }

    public new async ValueTask DisposeAsync()
    {
        Upstream.Stop();
        Upstream.Dispose();
        await base.DisposeAsync();
    }
}
