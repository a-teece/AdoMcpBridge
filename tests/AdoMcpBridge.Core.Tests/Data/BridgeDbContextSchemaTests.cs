using AdoMcpBridge.Core.Data;
using AdoMcpBridge.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

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
