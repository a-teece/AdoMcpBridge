using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.BlobStorage;
using AdoMcpBridge.Core.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests;

public class BridgeApiFactory : WebApplicationFactory<Program>
{
    public IEntraTokenClient EntraClient { get; } = Substitute.For<IEntraTokenClient>();
    public IKeyVaultEncryptor Encryptor { get; } = Substitute.For<IKeyVaultEncryptor>();
    public TestClock Clock { get; } = new();
    public InMemoryTokenStore Store { get; } = new();
    public IBlobSlotStore BlobSlotStore { get; } = Substitute.For<IBlobSlotStore>();
    public IAdoRestClient AdoRestClient { get; } = Substitute.For<IAdoRestClient>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration) because Program.cs
        // reads this eagerly at registration time; test config sources
        // are appended too late for eager reads under minimal hosting.
        builder.UseSetting("AdoMcp:Database:ConnectionString", "Server=localhost;Database=test;");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdoMcp:Issuer"] = "https://test.local",
                ["AdoMcp:UpstreamBaseUrl"] = "https://mcp.test.local",
                ["AdoMcp:Entra:TenantId"] = "tid",
                ["AdoMcp:Entra:ClientId"] = "wrapper-app",
                ["AdoMcp:Entra:Authority"] = "https://login.microsoftonline.com/tid/v2.0",
                ["AdoMcp:BlobStorage:AccountUri"] = "https://stadomcptest.blob.core.windows.net/",
            });
        });
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IEntraTokenClient>();
            s.RemoveAll<IKeyVaultEncryptor>();
            s.RemoveAll<IClock>();
            s.RemoveAll<ITokenStore>();
            s.RemoveAll<AdoMcpBridge.Core.OAuth.IAuthorizationSessionCache>();
            s.RemoveAll<IBlobSlotStore>();
            s.RemoveAll<IAdoRestClient>();
            s.AddSingleton(EntraClient);
            s.AddSingleton(Encryptor);
            s.AddSingleton<IClock>(Clock);
            s.AddSingleton<ITokenStore>(Store);
            s.AddSingleton<AdoMcpBridge.Core.OAuth.IAuthorizationSessionCache>(
                new AdoMcpBridge.Core.OAuth.InMemoryAuthorizationSessionCache(Clock));
            s.AddSingleton(BlobSlotStore);
            s.AddSingleton(AdoRestClient);

            Encryptor.EncryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
                .Returns(ci => new ValueTask<byte[]>(ci.Arg<byte[]>()));
            Encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
                .Returns(ci => new ValueTask<byte[]>(ci.Arg<byte[]>()));
        });
    }
}

public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
}
