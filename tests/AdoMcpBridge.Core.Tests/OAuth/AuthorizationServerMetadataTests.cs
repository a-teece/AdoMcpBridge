using System.Text.Json;
using AdoMcpBridge.Core.OAuth;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.OAuth;

public sealed class AuthorizationServerMetadataTests
{
    [Fact]
    public void Serialized_shape_matches_shared_contracts()
    {
        var m = AuthorizationServerMetadata.ForIssuer("https://bridge.example");
        var json = JsonSerializer.Serialize(m);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("issuer").GetString().Should().Be("https://bridge.example");
        root.GetProperty("authorization_endpoint").GetString().Should().Be("https://bridge.example/authorize");
        root.GetProperty("token_endpoint").GetString().Should().Be("https://bridge.example/token");
        root.GetProperty("registration_endpoint").GetString().Should().Be("https://bridge.example/register");
        root.GetProperty("revocation_endpoint").GetString().Should().Be("https://bridge.example/revoke");
        root.GetProperty("response_types_supported").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "code" });
        root.GetProperty("grant_types_supported").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "authorization_code", "refresh_token" });
        root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "S256" });
        root.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "none" });
        root.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "mcp" });
    }

    [Fact]
    public void Trailing_slash_in_issuer_is_trimmed()
    {
        var m = AuthorizationServerMetadata.ForIssuer("https://bridge.example/");
        m.Issuer.Should().Be("https://bridge.example");
        m.AuthorizationEndpoint.Should().Be("https://bridge.example/authorize");
    }
}
