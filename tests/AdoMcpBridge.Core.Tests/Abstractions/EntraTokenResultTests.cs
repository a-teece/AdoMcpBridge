namespace AdoMcpBridge.Core.Tests.Abstractions;

public sealed class EntraTokenResultTests
{
    [Fact]
    public void Two_equal_instances_compare_equal()
    {
        var expires = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        var a = new EntraTokenResult("at", "rt", expires, "oid", "upn@x");
        var b = new EntraTokenResult("at", "rt", expires, "oid", "upn@x");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Properties_round_trip()
    {
        var expires = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        var r = new EntraTokenResult("at", "rt", expires, "oid", "upn@x");
        Assert.Equal("at", r.AccessToken);
        Assert.Equal("rt", r.RefreshToken);
        Assert.Equal(expires, r.ExpiresAt);
        Assert.Equal("oid", r.UserObjectId);
        Assert.Equal("upn@x", r.UserPrincipalName);
    }
}
