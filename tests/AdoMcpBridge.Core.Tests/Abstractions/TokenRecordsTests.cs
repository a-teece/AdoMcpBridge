namespace AdoMcpBridge.Core.Tests.Abstractions;

public sealed class TokenRecordsTests
{
    [Fact]
    public void RegisteredClient_holds_all_properties()
    {
        var created = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        var rc = new RegisteredClient("cid", "name", new[] { "https://x/cb" }, created);
        Assert.Equal("cid", rc.ClientId);
        Assert.Equal("name", rc.ClientName);
        Assert.Single(rc.RedirectUris);
        Assert.Equal("https://x/cb", rc.RedirectUris[0]);
        Assert.Equal(created, rc.CreatedAt);
    }

    [Fact]
    public void AuthorizationCodeRecord_holds_all_properties()
    {
        var expires = new DateTimeOffset(2026, 6, 9, 0, 1, 0, TimeSpan.Zero);
        var ac = new AuthorizationCodeRecord(
            "code", "cid", "https://x/cb", "challenge", "S256",
            "ZW5j", "oid", "upn@x", expires);
        Assert.Equal("code", ac.Code);
        Assert.Equal("cid", ac.ClientId);
        Assert.Equal("https://x/cb", ac.RedirectUri);
        Assert.Equal("challenge", ac.PkceChallenge);
        Assert.Equal("S256", ac.PkceMethod);
        Assert.Equal("ZW5j", ac.EntraRefreshTokenEncrypted);
        Assert.Equal("oid", ac.UserObjectId);
        Assert.Equal("upn@x", ac.UserPrincipalName);
        Assert.Equal(expires, ac.ExpiresAt);
    }

    [Fact]
    public void TokenRecord_holds_all_properties()
    {
        var atExp = new DateTimeOffset(2026, 6, 9, 1, 0, 0, TimeSpan.Zero);
        var rtExp = new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
        var created = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        var tr = new TokenRecord(
            "ah", "rh", "cid", "ZW5j", "oid", "upn@x", atExp, rtExp, created);
        Assert.Equal("ah", tr.AccessTokenHash);
        Assert.Equal("rh", tr.RefreshTokenHash);
        Assert.Equal("cid", tr.ClientId);
        Assert.Equal("ZW5j", tr.EntraRefreshTokenEncrypted);
        Assert.Equal("oid", tr.UserObjectId);
        Assert.Equal("upn@x", tr.UserPrincipalName);
        Assert.Equal(atExp, tr.AccessTokenExpiresAt);
        Assert.Equal(rtExp, tr.RefreshTokenExpiresAt);
        Assert.Equal(created, tr.CreatedAt);
    }
}
