using AdoMcpBridge.Core.OAuth;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.OAuth;

public sealed class OAuthErrorTests
{
    [Fact]
    public void ToJson_emits_error_and_description_only()
    {
        var json = OAuthError.InvalidRequest("redirect_uri mismatch").ToJson();
        json.Should().Be("{\"error\":\"invalid_request\",\"error_description\":\"redirect_uri mismatch\"}");
    }

    [Fact]
    public void Codes_match_rfc6749()
    {
        OAuthError.InvalidClient("x").Code.Should().Be("invalid_client");
        OAuthError.InvalidGrant("x").Code.Should().Be("invalid_grant");
        OAuthError.UnsupportedGrantType("x").Code.Should().Be("unsupported_grant_type");
        OAuthError.UnauthorizedClient("x").Code.Should().Be("unauthorized_client");
        OAuthError.InvalidRequest("x").Code.Should().Be("invalid_request");
        OAuthError.ServerError("x").Code.Should().Be("server_error");
    }
}
