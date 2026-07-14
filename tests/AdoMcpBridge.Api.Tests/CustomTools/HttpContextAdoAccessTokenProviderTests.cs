using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class HttpContextAdoAccessTokenProviderTests
{
    private static HttpContextAdoAccessTokenProvider Create(HttpContext? context)
        => new(new HttpContextAccessor { HttpContext = context });

    [Fact]
    public void Extracts_bearer_token_from_authorization_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer caller-delegated-token";

        var provider = Create(context);

        provider.GetAccessToken().Should().Be("caller-delegated-token");
    }

    [Fact]
    public void Throws_when_no_http_context_is_active()
    {
        var provider = Create(context: null);

        var act = () => provider.GetAccessToken();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Throws_when_authorization_header_is_missing()
    {
        var provider = Create(new DefaultHttpContext());

        var act = () => provider.GetAccessToken();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Throws_when_authorization_header_is_not_a_bearer_scheme()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        var provider = Create(context);

        var act = () => provider.GetAccessToken();

        act.Should().Throw<InvalidOperationException>();
    }
}
