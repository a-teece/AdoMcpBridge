using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdoMcpBridge.Core.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can construct the context
/// without a running host. The connection string is a placeholder — migration
/// scaffolding only needs the model and provider, never a live connection.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Design-time only; invoked by EF tooling during migration scaffolding, never at runtime.")]
internal sealed class BridgeDbContextFactory : IDesignTimeDbContextFactory<BridgeDbContext>
{
    public BridgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BridgeDbContext>()
            .UseSqlServer("Server=localhost;Database=design-time;Trusted_Connection=true;TrustServerCertificate=true;")
            .Options;
        return new BridgeDbContext(options);
    }
}
