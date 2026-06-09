using System.Net.Sockets;
using AdoMcpBridge.Core.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace AdoMcpBridge.Core.Tests.Data;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;
    public bool DockerAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        DockerAvailable = await IsDockerReachableAsync();
        if (!DockerAvailable) return;

        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        var opts = new DbContextOptionsBuilder<BridgeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        await using var ctx = new BridgeDbContext(opts);
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    private static async Task<bool> IsDockerReachableAsync()
    {
        try
        {
            var path = Environment.OSVersion.Platform == PlatformID.Unix
                ? "/var/run/docker.sock" : null;
            if (path is null || !File.Exists(path)) return false;
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await s.ConnectAsync(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}

[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
