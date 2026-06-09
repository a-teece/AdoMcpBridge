using System.Text;
using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Core.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.Middleware;

public sealed class EntraTokenSwapMiddlewareTests
{
    private static TokenRecord Record() => new(
        AccessTokenHash: "ah",
        RefreshTokenHash: "rh",
        ClientId: "cid",
        EntraRefreshTokenEncrypted: Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        UserObjectId: "oid",
        UserPrincipalName: "u@example.com",
        AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
        RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(14),
        CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

    [Fact]
    public async Task Replaces_authorization_header_with_ado_token()
    {
        var encryptor = Substitute.For<IKeyVaultEncryptor>();
        encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<byte[]>(Encoding.UTF8.GetBytes("entra-refresh")));

        var entra = Substitute.For<IEntraTokenClient>();
        entra.AcquireAdoTokenAsync("entra-refresh", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EntraTokenResult>(new EntraTokenResult(
                AccessToken: "ado-access-xyz",
                RefreshToken: "new-refresh",
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(50),
                UserObjectId: "oid",
                UserPrincipalName: "u@example.com")));

        var ctx = new DefaultHttpContext();
        ctx.Items[HttpContextItemKeys.TokenRecord] = Record();
        ctx.Request.Headers["Authorization"] = "Bearer opaque";

        var mw = new EntraTokenSwapMiddleware(_ => Task.CompletedTask, NullLogger<EntraTokenSwapMiddleware>.Instance);
        await mw.InvokeAsync(ctx, encryptor, entra);

        ctx.Request.Headers["Authorization"].ToString().Should().Be("Bearer ado-access-xyz");
    }

    [Fact]
    public async Task Missing_token_record_returns_500_internal_error()
    {
        var encryptor = Substitute.For<IKeyVaultEncryptor>();
        var entra = Substitute.For<IEntraTokenClient>();
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var mw = new EntraTokenSwapMiddleware(_ => Task.CompletedTask, NullLogger<EntraTokenSwapMiddleware>.Instance);
        await mw.InvokeAsync(ctx, encryptor, entra);

        ctx.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Entra_failure_returns_502_with_translated_body()
    {
        var encryptor = Substitute.For<IKeyVaultEncryptor>();
        encryptor.DecryptAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<byte[]>(Encoding.UTF8.GetBytes("entra-refresh")));

        var entra = Substitute.For<IEntraTokenClient>();
        entra.AcquireAdoTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<EntraTokenResult>>(_ => throw new InvalidOperationException("upstream boom"));

        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Items[HttpContextItemKeys.TokenRecord] = Record();
        ctx.Request.Headers["Authorization"] = "Bearer opaque";

        var mw = new EntraTokenSwapMiddleware(_ => Task.CompletedTask, NullLogger<EntraTokenSwapMiddleware>.Instance);
        await mw.InvokeAsync(ctx, encryptor, entra);

        ctx.Response.StatusCode.Should().Be(502);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().NotContain("upstream boom");
        body.Should().Contain("entra_unavailable");
    }
}
