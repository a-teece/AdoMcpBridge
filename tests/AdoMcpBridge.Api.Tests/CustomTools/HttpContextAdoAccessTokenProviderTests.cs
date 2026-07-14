using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.Proxy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class HttpContextAdoAccessTokenProviderTests
{
    private static HttpContextAdoAccessTokenProvider Create(HttpContext? context)
        => new(new HttpContextAccessor { HttpContext = context });

    [Fact]
    public void Returns_the_ado_rest_token_stashed_on_http_context_items()
    {
        var context = new DefaultHttpContext();
        context.Items[HttpContextItemKeys.AdoRestAccessToken] = "caller-delegated-token";

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
    public void Throws_when_ado_rest_token_item_is_missing()
    {
        var provider = Create(new DefaultHttpContext());

        var act = () => provider.GetAccessToken();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Throws_when_ado_rest_token_item_is_empty()
    {
        var context = new DefaultHttpContext();
        context.Items[HttpContextItemKeys.AdoRestAccessToken] = string.Empty;

        var provider = Create(context);

        var act = () => provider.GetAccessToken();

        act.Should().Throw<InvalidOperationException>();
    }
}
