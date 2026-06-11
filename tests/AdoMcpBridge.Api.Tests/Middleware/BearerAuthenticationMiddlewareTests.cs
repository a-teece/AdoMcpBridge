using System.Text;
using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Core.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.Middleware;

public sealed class BearerAuthenticationMiddlewareTests
{
    private static TokenRecord SampleRecord(DateTimeOffset expires) => new(
        AccessTokenHash: TokenHasher.Sha256Hex("opaque-access"),
        RefreshTokenHash: "rh",
        ClientId: "cid",
        EntraRefreshTokenEncrypted: "enc",
        UserObjectId: "oid",
        UserPrincipalName: "u@example.com",
        AccessTokenExpiresAt: expires,
        RefreshTokenExpiresAt: expires.AddDays(14),
        CreatedAt: expires.AddHours(-1));

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
    }

    private static Microsoft.Extensions.Options.IOptions<Api.Options.AdoMcpOptions> TestOpts() =>
        Microsoft.Extensions.Options.Options.Create(new Api.Options.AdoMcpOptions { Issuer = "https://test.local" });

    [Fact]
    public async Task Missing_authorization_header_returns_401_with_www_authenticate()
    {
        var store = Substitute.For<ITokenStore>();
        var clock = new FixedClock();
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var mw = new BearerAuthenticationMiddleware(_ => Task.CompletedTask, NullLogger<BearerAuthenticationMiddleware>.Instance);
        await mw.InvokeAsync(ctx, store, clock, TestOpts());

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Response.Headers["WWW-Authenticate"].ToString().Should().StartWith("Bearer");
    }

    [Fact]
    public async Task Unknown_bearer_returns_401_invalid_token()
    {
        var store = Substitute.For<ITokenStore>();
        store.FindByAccessTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TokenRecord?)null);
        var clock = new FixedClock();
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Request.Headers["Authorization"] = "Bearer bogus";

        var mw = new BearerAuthenticationMiddleware(_ => Task.CompletedTask, NullLogger<BearerAuthenticationMiddleware>.Instance);
        await mw.InvokeAsync(ctx, store, clock, TestOpts());

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Response.Headers["WWW-Authenticate"].ToString().Should().Contain("invalid_token");
    }

    [Fact]
    public async Task Expired_bearer_returns_401_invalid_token()
    {
        var clock = new FixedClock();
        var record = SampleRecord(expires: clock.UtcNow.AddMinutes(-1));
        var store = Substitute.For<ITokenStore>();
        store.FindByAccessTokenHashAsync(TokenHasher.Sha256Hex("opaque-access"), Arg.Any<CancellationToken>())
            .Returns(record);

        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Request.Headers["Authorization"] = "Bearer opaque-access";

        var mw = new BearerAuthenticationMiddleware(_ => Task.CompletedTask, NullLogger<BearerAuthenticationMiddleware>.Instance);
        await mw.InvokeAsync(ctx, store, clock, TestOpts());

        ctx.Response.StatusCode.Should().Be(401);
        ctx.Response.Headers["WWW-Authenticate"].ToString().Should().Contain("invalid_token");
    }

    [Fact]
    public async Task Valid_bearer_stashes_record_and_calls_next()
    {
        var clock = new FixedClock();
        var record = SampleRecord(expires: clock.UtcNow.AddMinutes(30));
        var store = Substitute.For<ITokenStore>();
        store.FindByAccessTokenHashAsync(TokenHasher.Sha256Hex("opaque-access"), Arg.Any<CancellationToken>())
            .Returns(record);

        var nextCalled = false;
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Request.Headers["Authorization"] = "Bearer opaque-access";

        var mw = new BearerAuthenticationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<BearerAuthenticationMiddleware>.Instance);
        await mw.InvokeAsync(ctx, store, clock, TestOpts());

        nextCalled.Should().BeTrue();
        ctx.Items[HttpContextItemKeys.TokenRecord].Should().BeSameAs(record);
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Authorization_value_is_never_logged()
    {
        var store = Substitute.For<ITokenStore>();
        store.FindByAccessTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TokenRecord?)null);
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Request.Headers["Authorization"] = "Bearer SECRET-LITERAL";

        var mw = new BearerAuthenticationMiddleware(_ => Task.CompletedTask, NullLogger<BearerAuthenticationMiddleware>.Instance);
        await mw.InvokeAsync(ctx, store, new FixedClock(), TestOpts());

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body, Encoding.UTF8).ReadToEndAsync();
        body.Should().NotContain("SECRET-LITERAL");
    }
}
