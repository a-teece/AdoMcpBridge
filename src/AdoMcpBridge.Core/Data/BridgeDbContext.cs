using AdoMcpBridge.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdoMcpBridge.Core.Data;

public sealed class BridgeDbContext : DbContext
{
    public BridgeDbContext(DbContextOptions<BridgeDbContext> options) : base(options) { }

    internal DbSet<ClientEntity> Clients => Set<ClientEntity>();
    internal DbSet<AuthorizationCodeEntity> AuthorizationCodes => Set<AuthorizationCodeEntity>();
    internal DbSet<TokenEntity> Tokens => Set<TokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientEntity>(e =>
        {
            e.ToTable("Clients");
            e.HasKey(x => x.ClientId);
            e.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
            e.Property(x => x.ClientName).HasMaxLength(256).IsRequired();
            e.Property(x => x.RedirectUrisJson).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<AuthorizationCodeEntity>(e =>
        {
            e.ToTable("AuthorizationCodes");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
            e.Property(x => x.RedirectUri).HasMaxLength(2048).IsRequired();
            e.Property(x => x.PkceChallenge).HasMaxLength(128).IsRequired();
            e.Property(x => x.PkceMethod).HasMaxLength(16).IsRequired();
            e.Property(x => x.EntraRefreshTokenEncrypted).IsRequired();
            e.Property(x => x.UserObjectId).HasMaxLength(64).IsRequired();
            e.Property(x => x.UserPrincipalName).HasMaxLength(256).IsRequired();
            e.Property(x => x.ExpiresAt).IsRequired();
            e.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<TokenEntity>(e =>
        {
            e.ToTable("Tokens");
            e.HasKey(x => x.AccessTokenHash);
            e.Property(x => x.AccessTokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.RefreshTokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
            e.Property(x => x.EntraRefreshTokenEncrypted).IsRequired();
            e.Property(x => x.UserObjectId).HasMaxLength(64).IsRequired();
            e.Property(x => x.UserPrincipalName).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasIndex(x => x.RefreshTokenExpiresAt);
        });
    }
}
