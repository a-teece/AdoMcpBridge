# Token Store Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the production persistence layer for the ADO MCP Bridge — an EF Core 9 `BridgeDbContext` against real SQL Server, an `EfTokenStore` implementation of `ITokenStore`, an `InitialSchema` migration, a Key Vault-backed `IKeyVaultEncryptor` implementation, integration tests using a real SQL container, and DI registration.

**Architecture:** EF Core 9 (net10.0) DbContext with three entity types (`ClientEntity`, `AuthorizationCodeEntity`, `TokenEntity`) mapped via Fluent API. Public records from `Abstractions/` are mapping DTOs at the store boundary; entities are persistence-only. The encryptor uses `Azure.Security.KeyVault.Keys.Cryptography.CryptographyClient` with the DEK named in `AdoMcp:KeyVault:DekName` (RSA-OAEP-256). All wiring is exposed via a single `AddBridgeDataServices` extension.

**Tech Stack:** .NET 10, EF Core 9, `Microsoft.EntityFrameworkCore.SqlServer`, `Azure.Security.KeyVault.Keys`, `Azure.Identity`, xUnit, NSubstitute, `Testcontainers.MsSql`, `Xunit.SkippableFact`.

---

## File Map

Create:
- `src/AdoMcpBridge.Core/Data/BridgeDbContext.cs`
- `src/AdoMcpBridge.Core/Data/Entities/ClientEntity.cs`
- `src/AdoMcpBridge.Core/Data/Entities/AuthorizationCodeEntity.cs`
- `src/AdoMcpBridge.Core/Data/Entities/TokenEntity.cs`
- `src/AdoMcpBridge.Core/Data/EfTokenStore.cs`
- `src/AdoMcpBridge.Core/Data/Migrations/<timestamp>_InitialSchema.cs` (generated)
- `src/AdoMcpBridge.Core/Data/Migrations/BridgeDbContextModelSnapshot.cs` (generated)
- `src/AdoMcpBridge.Core/KeyVault/KeyVaultEncryptor.cs`
- `src/AdoMcpBridge.Core/KeyVault/KeyVaultOptions.cs`
- `src/AdoMcpBridge.Core/DependencyInjection/DataServiceCollectionExtensions.cs`
- `tests/AdoMcpBridge.Core.Tests/Data/SqlServerFixture.cs`
- `tests/AdoMcpBridge.Core.Tests/Data/EfTokenStoreTests.cs`
- `tests/AdoMcpBridge.Core.Tests/Data/BridgeDbContextSchemaTests.cs`
- `tests/AdoMcpBridge.Core.Tests/KeyVault/KeyVaultEncryptorTests.cs`
- `tests/AdoMcpBridge.Core.Tests/DependencyInjection/DataServiceCollectionExtensionsTests.cs`

Modify:
- `src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj` — add EF Core + KV packages.
- `tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj` — add Testcontainers + SkippableFact.

---

### Task 1: Add NuGet package references

**Files:**
- Modify: `src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`
- Modify: `tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`

- [ ] **Step 1: Add Core packages**

Add to `src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj` inside an `<ItemGroup>`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
<PackageReference Include="Azure.Identity" Version="1.13.1" />
<PackageReference Include="Azure.Security.KeyVault.Keys" Version="4.7.0" />
```

- [ ] **Step 2: Add Test packages**

Add to `tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`:

```xml
<PackageReference Include="Testcontainers.MsSql" Version="4.0.0" />
<PackageReference Include="Xunit.SkippableFact" Version="1.4.13" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.0" />
<PackageReference Include="NSubstitute" Version="5.3.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Memory" Version="9.0.0" />
```

- [ ] **Step 3: Restore and build**

Run: `dotnet restore && dotnet build`
Expected: PASS (no compilation errors).

- [ ] **Step 4: Commit**

```bash
git add src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj
git commit -m "chore: add EF Core 9 and Key Vault packages for token store"
```

---

### Task 2: Define EF entity types

**Files:**
- Create: `src/AdoMcpBridge.Core/Data/Entities/ClientEntity.cs`
- Create: `src/AdoMcpBridge.Core/Data/Entities/AuthorizationCodeEntity.cs`
- Create: `src/AdoMcpBridge.Core/Data/Entities/TokenEntity.cs`

- [ ] **Step 1: Write `ClientEntity`**

```csharp
namespace AdoMcpBridge.Core.Data.Entities;

internal sealed class ClientEntity
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string RedirectUrisJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Write `AuthorizationCodeEntity`**

```csharp
namespace AdoMcpBridge.Core.Data.Entities;

internal sealed class AuthorizationCodeEntity
{
    public string Code { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string PkceChallenge { get; set; } = string.Empty;
    public string PkceMethod { get; set; } = "S256";
    public string EntraRefreshTokenEncrypted { get; set; } = string.Empty;
    public string UserObjectId { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
```

- [ ] **Step 3: Write `TokenEntity`**

```csharp
namespace AdoMcpBridge.Core.Data.Entities;

internal sealed class TokenEntity
{
    public string AccessTokenHash { get; set; } = string.Empty;
    public string RefreshTokenHash { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string EntraRefreshTokenEncrypted { get; set; } = string.Empty;
    public string UserObjectId { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public DateTimeOffset RefreshTokenExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Data/Entities/
git commit -m "feat: add EF entity types for token store"
```

---

### Task 3: BridgeDbContext schema test (red)

**Files:**
- Create: `tests/AdoMcpBridge.Core.Tests/Data/BridgeDbContextSchemaTests.cs`

- [ ] **Step 1: Write failing schema-shape test**

```csharp
using AdoMcpBridge.Core.Data;
using AdoMcpBridge.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdoMcpBridge.Core.Tests.Data;

public sealed class BridgeDbContextSchemaTests
{
    private static BridgeDbContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<BridgeDbContext>()
            .UseSqlServer("Server=.;Database=ignored;Trusted_Connection=true;")
            .Options;
        return new BridgeDbContext(opts);
    }

    [Fact]
    public void Clients_table_has_ClientId_pk_and_string64()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(ClientEntity))!;
        var pk = entity.FindPrimaryKey()!;
        Assert.Single(pk.Properties);
        Assert.Equal(nameof(ClientEntity.ClientId), pk.Properties[0].Name);
        Assert.Equal(64, pk.Properties[0].GetMaxLength());
    }

    [Fact]
    public void AuthorizationCodes_indexed_on_ExpiresAt()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(AuthorizationCodeEntity))!;
        Assert.Contains(entity.GetIndexes(),
            i => i.Properties.Count == 1 &&
                 i.Properties[0].Name == nameof(AuthorizationCodeEntity.ExpiresAt));
    }

    [Fact]
    public void Tokens_have_pk_unique_refresh_and_expiry_index()
    {
        using var ctx = NewContext();
        var entity = ctx.Model.FindEntityType(typeof(TokenEntity))!;
        var pk = entity.FindPrimaryKey()!;
        Assert.Equal(nameof(TokenEntity.AccessTokenHash), pk.Properties[0].Name);

        Assert.Contains(entity.GetIndexes(),
            i => i.IsUnique &&
                 i.Properties.Count == 1 &&
                 i.Properties[0].Name == nameof(TokenEntity.RefreshTokenHash));

        Assert.Contains(entity.GetIndexes(),
            i => i.Properties.Count == 1 &&
                 i.Properties[0].Name == nameof(TokenEntity.RefreshTokenExpiresAt));
    }
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter "FullyQualifiedName~BridgeDbContextSchemaTests"`
Expected: FAIL with "type or namespace `BridgeDbContext` could not be found" (compile error).

- [ ] **Step 3: Commit (red)**

```bash
git add tests/AdoMcpBridge.Core.Tests/Data/BridgeDbContextSchemaTests.cs
git commit -m "test: add failing BridgeDbContext schema tests"
```

---

### Task 4: Implement BridgeDbContext (green)

**Files:**
- Create: `src/AdoMcpBridge.Core/Data/BridgeDbContext.cs`

- [ ] **Step 1: Write the DbContext**

```csharp
using AdoMcpBridge.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdoMcpBridge.Core.Data;

public sealed class BridgeDbContext : DbContext
{
    public BridgeDbContext(DbContextOptions<BridgeDbContext> options) : base(options) { }

    internal DbSet<ClientEntity> Clients => Set<ClientEntity>();
    internal DbSet<AuthorizationCodeEntity> AuthorizationCodes => Set<AuthorizationCodeEntity>();
    internal DbSet<TokenEntity> Tokens => Set<TokenEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ClientEntity>(e =>
        {
            e.ToTable("Clients");
            e.HasKey(x => x.ClientId);
            e.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
            e.Property(x => x.ClientName).HasMaxLength(256).IsRequired();
            e.Property(x => x.RedirectUrisJson).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
        });

        b.Entity<AuthorizationCodeEntity>(e =>
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

        b.Entity<TokenEntity>(e =>
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
```

- [ ] **Step 2: Run schema tests**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter "FullyQualifiedName~BridgeDbContextSchemaTests"`
Expected: PASS (3 tests).

- [ ] **Step 3: Commit**

```bash
git add src/AdoMcpBridge.Core/Data/BridgeDbContext.cs
git commit -m "feat: add BridgeDbContext with Fluent API schema"
```

---

### Task 5: SQL Server Testcontainers fixture

**Files:**
- Create: `tests/AdoMcpBridge.Core.Tests/Data/SqlServerFixture.cs`

- [ ] **Step 1: Write the fixture**

```csharp
using System.Net.Sockets;
using AdoMcpBridge.Core.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace AdoMcpBridge.Core.Tests.Data;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;
    public bool DockerAvailable { get; private set; }

    public async ValueTask InitializeAsync()
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

    public async ValueTask DisposeAsync()
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
            await s.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(path));
            return true;
        }
        catch { return false; }
    }
}

[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }
```

- [ ] **Step 2: Build**

Run: `dotnet build tests/AdoMcpBridge.Core.Tests`
Expected: PASS. (`Database.MigrateAsync` requires migration to exist; we'll add it in Task 7. For now, this file compiles but won't be exercised yet.)

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Core.Tests/Data/SqlServerFixture.cs
git commit -m "test: add SQL Server Testcontainers fixture"
```

---

### Task 6: EfTokenStore failing integration tests

**Files:**
- Create: `tests/AdoMcpBridge.Core.Tests/Data/EfTokenStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdoMcpBridge.Core.Tests.Data;

[Collection("SqlServer")]
public sealed class EfTokenStoreTests
{
    private readonly SqlServerFixture _fx;
    public EfTokenStoreTests(SqlServerFixture fx) => _fx = fx;

    private EfTokenStore NewStore()
    {
        var opts = new DbContextOptionsBuilder<BridgeDbContext>()
            .UseSqlServer(_fx.ConnectionString)
            .Options;
        return new EfTokenStore(new BridgeDbContext(opts));
    }

    [SkippableFact]
    public async Task AddClient_then_Find_roundtrips()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        var client = new RegisteredClient(
            ClientId: Guid.NewGuid().ToString("N"),
            ClientName: "claude-code",
            RedirectUris: new[] { "https://example.test/cb" },
            CreatedAt: DateTimeOffset.UtcNow);

        await store.AddClientAsync(client, CancellationToken.None);
        var found = await store.FindClientAsync(client.ClientId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(client.ClientId, found!.ClientId);
        Assert.Equal(client.ClientName, found.ClientName);
        Assert.Equal(client.RedirectUris, found.RedirectUris);
    }

    [SkippableFact]
    public async Task ConsumeAuthorizationCode_returns_and_deletes()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        var code = new AuthorizationCodeRecord(
            Code: Guid.NewGuid().ToString("N"),
            ClientId: "c1",
            RedirectUri: "https://example.test/cb",
            PkceChallenge: "challenge",
            PkceMethod: "S256",
            EntraRefreshTokenEncrypted: "AAAA",
            UserObjectId: "oid",
            UserPrincipalName: "u@example.test",
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(60));

        await store.AddAuthorizationCodeAsync(code, CancellationToken.None);
        var first = await store.ConsumeAuthorizationCodeAsync(code.Code, CancellationToken.None);
        var second = await store.ConsumeAuthorizationCodeAsync(code.Code, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(code.Code, first!.Code);
        Assert.Null(second);
    }

    [SkippableFact]
    public async Task AddToken_FindByAccess_FindByRefresh_Revoke_Replace()
    {
        Skip.IfNot(_fx.DockerAvailable, "Docker unavailable");
        var store = NewStore();
        var token = new TokenRecord(
            AccessTokenHash: NewHash(),
            RefreshTokenHash: NewHash(),
            ClientId: "c1",
            EntraRefreshTokenEncrypted: "AAAA",
            UserObjectId: "oid",
            UserPrincipalName: "u@example.test",
            AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(14),
            CreatedAt: DateTimeOffset.UtcNow);

        await store.AddTokenAsync(token, CancellationToken.None);

        var byAccess = await store.FindByAccessTokenHashAsync(token.AccessTokenHash, CancellationToken.None);
        var byRefresh = await store.FindByRefreshTokenHashAsync(token.RefreshTokenHash, CancellationToken.None);
        Assert.NotNull(byAccess);
        Assert.NotNull(byRefresh);

        var replacement = token with { AccessTokenHash = NewHash(), RefreshTokenHash = NewHash() };
        await store.ReplaceTokenAsync(token, replacement, CancellationToken.None);

        Assert.Null(await store.FindByAccessTokenHashAsync(token.AccessTokenHash, CancellationToken.None));
        Assert.NotNull(await store.FindByAccessTokenHashAsync(replacement.AccessTokenHash, CancellationToken.None));

        await store.RevokeTokenAsync(replacement.RefreshTokenHash, CancellationToken.None);
        Assert.Null(await store.FindByRefreshTokenHashAsync(replacement.RefreshTokenHash, CancellationToken.None));
    }

    private static string NewHash() =>
        Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant().PadRight(64, '0')[..64];
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter "FullyQualifiedName~EfTokenStoreTests"`
Expected: FAIL — `EfTokenStore` type does not exist (compile error).

- [ ] **Step 3: Commit (red)**

```bash
git add tests/AdoMcpBridge.Core.Tests/Data/EfTokenStoreTests.cs
git commit -m "test: add failing EfTokenStore integration tests"
```

---

### Task 7: Implement EfTokenStore + InitialSchema migration

**Files:**
- Create: `src/AdoMcpBridge.Core/Data/EfTokenStore.cs`
- Create: migration files under `src/AdoMcpBridge.Core/Data/Migrations/`

- [ ] **Step 1: Implement `EfTokenStore`**

```csharp
using System.Text.Json;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdoMcpBridge.Core.Data;

public sealed class EfTokenStore : ITokenStore
{
    private readonly BridgeDbContext _db;
    public EfTokenStore(BridgeDbContext db) => _db = db;

    public async ValueTask<RegisteredClient?> FindClientAsync(string clientId, CancellationToken ct)
    {
        var e = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, ct);
        return e is null ? null : ToRecord(e);
    }

    public async ValueTask AddClientAsync(RegisteredClient client, CancellationToken ct)
    {
        _db.Clients.Add(new ClientEntity
        {
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            RedirectUrisJson = JsonSerializer.Serialize(client.RedirectUris),
            CreatedAt = client.CreatedAt,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask AddAuthorizationCodeAsync(AuthorizationCodeRecord code, CancellationToken ct)
    {
        _db.AuthorizationCodes.Add(new AuthorizationCodeEntity
        {
            Code = code.Code,
            ClientId = code.ClientId,
            RedirectUri = code.RedirectUri,
            PkceChallenge = code.PkceChallenge,
            PkceMethod = code.PkceMethod,
            EntraRefreshTokenEncrypted = code.EntraRefreshTokenEncrypted,
            UserObjectId = code.UserObjectId,
            UserPrincipalName = code.UserPrincipalName,
            ExpiresAt = code.ExpiresAt,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask<AuthorizationCodeRecord?> ConsumeAuthorizationCodeAsync(string code, CancellationToken ct)
    {
        var e = await _db.AuthorizationCodes.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (e is null) return null;
        _db.AuthorizationCodes.Remove(e);
        await _db.SaveChangesAsync(ct);
        return new AuthorizationCodeRecord(
            e.Code, e.ClientId, e.RedirectUri, e.PkceChallenge, e.PkceMethod,
            e.EntraRefreshTokenEncrypted, e.UserObjectId, e.UserPrincipalName, e.ExpiresAt);
    }

    public async ValueTask AddTokenAsync(TokenRecord token, CancellationToken ct)
    {
        _db.Tokens.Add(ToEntity(token));
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask<TokenRecord?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct)
    {
        var e = await _db.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.AccessTokenHash == accessTokenHash, ct);
        return e is null ? null : ToRecord(e);
    }

    public async ValueTask<TokenRecord?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct)
    {
        var e = await _db.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.RefreshTokenHash == refreshTokenHash, ct);
        return e is null ? null : ToRecord(e);
    }

    public async ValueTask RevokeTokenAsync(string refreshTokenHash, CancellationToken ct)
    {
        var e = await _db.Tokens.FirstOrDefaultAsync(t => t.RefreshTokenHash == refreshTokenHash, ct);
        if (e is null) return;
        _db.Tokens.Remove(e);
        await _db.SaveChangesAsync(ct);
    }

    public async ValueTask ReplaceTokenAsync(TokenRecord oldToken, TokenRecord newToken, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var existing = await _db.Tokens.FirstOrDefaultAsync(t => t.AccessTokenHash == oldToken.AccessTokenHash, ct);
        if (existing is not null) _db.Tokens.Remove(existing);
        _db.Tokens.Add(ToEntity(newToken));
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static RegisteredClient ToRecord(ClientEntity e) => new(
        e.ClientId, e.ClientName,
        JsonSerializer.Deserialize<List<string>>(e.RedirectUrisJson) ?? new List<string>(),
        e.CreatedAt);

    private static TokenRecord ToRecord(TokenEntity e) => new(
        e.AccessTokenHash, e.RefreshTokenHash, e.ClientId, e.EntraRefreshTokenEncrypted,
        e.UserObjectId, e.UserPrincipalName, e.AccessTokenExpiresAt, e.RefreshTokenExpiresAt, e.CreatedAt);

    private static TokenEntity ToEntity(TokenRecord t) => new()
    {
        AccessTokenHash = t.AccessTokenHash,
        RefreshTokenHash = t.RefreshTokenHash,
        ClientId = t.ClientId,
        EntraRefreshTokenEncrypted = t.EntraRefreshTokenEncrypted,
        UserObjectId = t.UserObjectId,
        UserPrincipalName = t.UserPrincipalName,
        AccessTokenExpiresAt = t.AccessTokenExpiresAt,
        RefreshTokenExpiresAt = t.RefreshTokenExpiresAt,
        CreatedAt = t.CreatedAt,
    };
}
```

- [ ] **Step 2: Generate the initial migration**

Run from repo root:

```
dotnet tool install --global dotnet-ef --version 9.0.0 || true
dotnet ef migrations add InitialSchema \
  --project src/AdoMcpBridge.Core \
  --startup-project src/AdoMcpBridge.Core \
  --output-dir Data/Migrations
```

Expected: Two files generated under `src/AdoMcpBridge.Core/Data/Migrations/`: `<ts>_InitialSchema.cs` + `BridgeDbContextModelSnapshot.cs`.

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 4: Run integration tests**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter "FullyQualifiedName~EfTokenStoreTests"`
Expected: PASS if Docker available; otherwise SKIPPED with reason "Docker unavailable".

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Data/EfTokenStore.cs src/AdoMcpBridge.Core/Data/Migrations/
git commit -m "feat: add EfTokenStore and InitialSchema migration"
```

---

### Task 8: KeyVaultOptions + failing encryptor unit tests

**Files:**
- Create: `src/AdoMcpBridge.Core/KeyVault/KeyVaultOptions.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/KeyVault/KeyVaultEncryptorTests.cs`

- [ ] **Step 1: Write `KeyVaultOptions`**

```csharp
namespace AdoMcpBridge.Core.KeyVault;

public sealed class KeyVaultOptions
{
    public const string SectionName = "AdoMcp:KeyVault";
    public string VaultUri { get; set; } = string.Empty;
    public string DekName { get; set; } = "token-dek";
}
```

- [ ] **Step 2: Write failing unit tests**

```csharp
using System.Threading;
using System.Threading.Tasks;
using AdoMcpBridge.Core.KeyVault;
using Azure.Security.KeyVault.Keys.Cryptography;
using NSubstitute;
using Xunit;

namespace AdoMcpBridge.Core.Tests.KeyVault;

public sealed class KeyVaultEncryptorTests
{
    [Fact]
    public async Task EncryptAsync_uses_RsaOaep256_and_returns_ciphertext()
    {
        var crypto = Substitute.For<CryptographyClient>();
        var plaintext = new byte[] { 1, 2, 3 };
        var ciphertext = new byte[] { 9, 9, 9 };

        crypto.EncryptAsync(EncryptionAlgorithm.RsaOaep256, Arg.Is<byte[]>(b => b.SequenceEqual(plaintext)), Arg.Any<CancellationToken>())
              .Returns(Azure.Response.FromValue(
                  CryptographyModelFactory.EncryptResult(keyId: "kid", ciphertext: ciphertext, algorithm: EncryptionAlgorithm.RsaOaep256),
                  Substitute.For<Azure.Response>()));

        var encryptor = new KeyVaultEncryptor(crypto);
        var result = await encryptor.EncryptAsync(plaintext, CancellationToken.None);

        Assert.Equal(ciphertext, result);
    }

    [Fact]
    public async Task DecryptAsync_uses_RsaOaep256_and_returns_plaintext()
    {
        var crypto = Substitute.For<CryptographyClient>();
        var ciphertext = new byte[] { 9, 9, 9 };
        var plaintext = new byte[] { 1, 2, 3 };

        crypto.DecryptAsync(DecryptionAlgorithm.RsaOaep256, Arg.Is<byte[]>(b => b.SequenceEqual(ciphertext)), Arg.Any<CancellationToken>())
              .Returns(Azure.Response.FromValue(
                  CryptographyModelFactory.DecryptResult(keyId: "kid", plaintext: plaintext, algorithm: DecryptionAlgorithm.RsaOaep256),
                  Substitute.For<Azure.Response>()));

        var encryptor = new KeyVaultEncryptor(crypto);
        var result = await encryptor.DecryptAsync(ciphertext, CancellationToken.None);

        Assert.Equal(plaintext, result);
    }
}
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter "FullyQualifiedName~KeyVaultEncryptorTests"`
Expected: FAIL — `KeyVaultEncryptor` does not exist (compile error).

- [ ] **Step 4: Commit (red)**

```bash
git add src/AdoMcpBridge.Core/KeyVault/KeyVaultOptions.cs tests/AdoMcpBridge.Core.Tests/KeyVault/KeyVaultEncryptorTests.cs
git commit -m "test: add failing KeyVaultEncryptor unit tests"
```

---

### Task 9: Implement KeyVaultEncryptor (green)

**Files:**
- Create: `src/AdoMcpBridge.Core/KeyVault/KeyVaultEncryptor.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using AdoMcpBridge.Core.Abstractions;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace AdoMcpBridge.Core.KeyVault;

public sealed class KeyVaultEncryptor : IKeyVaultEncryptor
{
    private readonly CryptographyClient _client;

    public KeyVaultEncryptor(CryptographyClient client) => _client = client;

    public async ValueTask<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct)
    {
        var result = await _client.EncryptAsync(EncryptionAlgorithm.RsaOaep256, plaintext, ct);
        return result.Ciphertext;
    }

    public async ValueTask<byte[]> DecryptAsync(byte[] ciphertext, CancellationToken ct)
    {
        var result = await _client.DecryptAsync(DecryptionAlgorithm.RsaOaep256, ciphertext, ct);
        return result.Plaintext;
    }
}
```

- [ ] **Step 2: Run unit tests**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter "FullyQualifiedName~KeyVaultEncryptorTests"`
Expected: PASS (2 tests).

- [ ] **Step 3: Commit**

```bash
git add src/AdoMcpBridge.Core/KeyVault/KeyVaultEncryptor.cs
git commit -m "feat: add Key Vault DEK encryptor using RSA-OAEP-256"
```

---

### Task 10: DI extension failing test

**Files:**
- Create: `tests/AdoMcpBridge.Core.Tests/DependencyInjection/DataServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Data;
using AdoMcpBridge.Core.DependencyInjection;
using AdoMcpBridge.Core.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdoMcpBridge.Core.Tests.DependencyInjection;

public sealed class DataServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBridgeDataServices_registers_DbContext_TokenStore_and_Options()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdoMcp:Database:ConnectionString"] = "Server=(local);Database=Test;Trusted_Connection=true;",
                ["AdoMcp:KeyVault:VaultUri"] = "https://example.vault.azure.net/",
                ["AdoMcp:KeyVault:DekName"] = "token-dek",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddBridgeDataServices(cfg);
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<BridgeDbContext>());
        Assert.IsType<EfTokenStore>(sp.GetRequiredService<ITokenStore>());
        var kvOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KeyVaultOptions>>().Value;
        Assert.Equal("token-dek", kvOpts.DekName);
    }
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter "FullyQualifiedName~DataServiceCollectionExtensionsTests"`
Expected: FAIL — `AddBridgeDataServices` not defined (compile error).

- [ ] **Step 3: Commit (red)**

```bash
git add tests/AdoMcpBridge.Core.Tests/DependencyInjection/DataServiceCollectionExtensionsTests.cs
git commit -m "test: add failing DI registration test for data services"
```

---

### Task 11: Implement DI extension (green)

**Files:**
- Create: `src/AdoMcpBridge.Core/DependencyInjection/DataServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the extension**

```csharp
using System.Diagnostics.CodeAnalysis;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Data;
using AdoMcpBridge.Core.KeyVault;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.DependencyInjection;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddBridgeDataServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration["AdoMcp:Database:ConnectionString"]
            ?? throw new InvalidOperationException("AdoMcp:Database:ConnectionString is required.");

        services.AddDbContext<BridgeDbContext>(o => o.UseSqlServer(connectionString));

        services.AddScoped<ITokenStore, EfTokenStore>();

        services.Configure<KeyVaultOptions>(configuration.GetSection(KeyVaultOptions.SectionName));

        services.AddSingleton(BuildCryptographyClient);
        services.AddSingleton<IKeyVaultEncryptor, KeyVaultEncryptor>();

        return services;
    }

    [ExcludeFromCodeCoverage(Justification = "Constructs a live Azure CryptographyClient; covered by integration tests against a real Key Vault, not unit-testable.")]
    private static CryptographyClient BuildCryptographyClient(IServiceProvider sp)
    {
        var opts = sp.GetRequiredService<IOptions<KeyVaultOptions>>().Value;
        var keyClient = new KeyClient(new Uri(opts.VaultUri), new DefaultAzureCredential());
        var key = keyClient.GetKey(opts.DekName).Value;
        return new CryptographyClient(key.Id, new DefaultAzureCredential());
    }
}
```

- [ ] **Step 2: Run DI test**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter "FullyQualifiedName~DataServiceCollectionExtensionsTests"`
Expected: PASS.

Note: the DI test uses an in-memory configuration and never resolves `IKeyVaultEncryptor`, so the live `CryptographyClient` factory is never executed during the test (the registration is lazy).

- [ ] **Step 3: Commit**

```bash
git add src/AdoMcpBridge.Core/DependencyInjection/DataServiceCollectionExtensions.cs
git commit -m "feat: add AddBridgeDataServices DI extension"
```

---

### Task 12: Full Core.Tests run + coverage spot check

**Files:** (none — verification only)

- [ ] **Step 1: Run all Core.Tests**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests`
Expected: PASS (integration tests SKIPPED if Docker unavailable; everything else passes).

- [ ] **Step 2: Run with coverage**

Run:
```
dotnet test tests/AdoMcpBridge.Core.Tests \
  /p:CollectCoverage=true \
  /p:Threshold=100 /p:ThresholdType=line,branch,method \
  /p:Exclude="[*]AdoMcpBridge.Core.Data.Migrations.*"
```
Expected: PASS coverage thresholds. Migrations namespace excluded because generated code is not unit-testable.

- [ ] **Step 3: If coverage fails**

If any non-migration line is uncovered, either add a unit test (preferred) or annotate the offending member with `[ExcludeFromCodeCoverage(Justification="<reason>")]`. The Foundation plan's ADOMCP002 analyzer enforces non-empty `Justification`.

- [ ] **Step 4: Commit any test additions if needed**

```bash
git add tests/AdoMcpBridge.Core.Tests/
git commit -m "test: cover remaining EfTokenStore branches"
```
(Skip if no changes.)

---

### Task 13: Open the PR

**Files:** (none)

- [ ] **Step 1: Push branch**

```bash
git push -u origin claude/token-store
```

- [ ] **Step 2: Open PR**

```bash
gh pr create --title "feat: token store (EF Core + Key Vault DEK encryptor)" --body "$(cat <<'EOF'
## Summary
- Adds `BridgeDbContext`, three EF entities, Fluent API schema, and `InitialSchema` migration.
- Adds `EfTokenStore` (production `ITokenStore`) with real-SQL integration tests via `Testcontainers.MsSql`, gated by Docker availability via `SkippableFact`.
- Adds `KeyVaultEncryptor` using `CryptographyClient` (RSA-OAEP-256) with NSubstitute unit tests.
- Adds `AddBridgeDataServices` DI extension.

## Review focus
- Schema constraints match `_shared-contracts.md` (PKs, max-length 64, unique refresh-hash index, expiry indexes).
- No EF in-memory provider anywhere — integration tests use a real SQL Server container.
- Encryptor never logs ciphertext/plaintext.
- `[ExcludeFromCodeCoverage]` only on the live `CryptographyClient` factory, with a `Justification`.

## Test plan
- [ ] `dotnet test tests/AdoMcpBridge.Core.Tests` — all green locally.
- [ ] Coverage at 100% with migrations excluded.
- [ ] CI green.
EOF
)"
```

---

## Self-Review Notes

**Spec coverage:**
- §3 "EF Core for the token store (real SQL, not in-memory provider)" — Tasks 4, 5, 6, 7 use `Testcontainers.MsSql`.
- §5 "Entra refresh tokens are encrypted at rest with a Key Vault-held DEK" — Tasks 8, 9 (RSA-OAEP-256 via `CryptographyClient`).
- §6 "Three tables: Clients, AuthorizationCodes, Tokens. MI auth, no SQL passwords. EF Core migrations" — Task 4 (3 tables), Task 7 (InitialSchema migration); MI is handled by the connection string (`Authentication=Active Directory Default`) coming from configuration, not by code in this plan.
- §7 "No EF in-memory provider. Integration tests use a real SQL container" — Task 5 fixture uses `MsSqlContainer`.
- §7 "[ExcludeFromCodeCoverage(Justification=…)]" — Task 11 annotates the live `CryptographyClient` factory.
- Shared contracts: namespaces (`AdoMcpBridge.Core.Data`, `AdoMcpBridge.Core.KeyVault`), file paths, `BridgeDbContext` exposing the three DbSets, PK and index rules, `KeyVaultOptions.SectionName = "AdoMcp:KeyVault"`, `DekName` default — all matched in Tasks 2, 4, 8.

**Placeholder scan:** No "TBD"/"similar to"/"appropriate handling" strings. Every code step contains full code. Every test step asserts concrete values.

**Type consistency:** `EfTokenStore` constructor takes `BridgeDbContext`; tests construct it the same way in Task 6. `KeyVaultEncryptor` constructor takes `CryptographyClient` in both Task 9 and the tests in Task 8. Method signatures match `ITokenStore` from `_shared-contracts.md` (all `ValueTask`, all `CancellationToken ct`). Records (`RegisteredClient`, `AuthorizationCodeRecord`, `TokenRecord`) are used with the exact property names from the contracts doc.

**Gaps explicitly delegated:**
- Migrations runtime application in production (Task 5 only migrates inside the test fixture). Production migration-on-startup belongs to the API host wiring in `2026-06-09-oauth-as.md` or an infra plan.
- Managed-identity SQL auth — handled at the configuration/connection-string layer, not by store code.
