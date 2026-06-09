using AdoMcpBridge.Core.OAuth;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.OAuth;

public sealed class AuthorizeRequestValidatorTests
{
    private readonly InMemoryTokenStore _store = new();

    private async Task SeedClient(string id, string redirect)
    {
        await _store.AddClientAsync(
            new RegisteredClient(id, "Test", new[] { redirect }, DateTimeOffset.UtcNow), default);
    }

    [Fact]
    public async Task Valid_request_returns_no_error()
    {
        await SeedClient("c1", "https://cb.example/cb");
        var v = new AuthorizeRequestValidator(_store);
        var (ok, err) = await v.ValidateAsync(new AuthorizeRequest(
            "code", "c1", "https://cb.example/cb", "abc123abc123abc123abc123abc123abc123abc123abc",
            "S256", "state-x", "mcp"), default);
        ok.Should().BeTrue();
        err.Should().BeNull();
    }

    [Theory]
    [InlineData("token")]
    [InlineData("")]
    public async Task Rejects_non_code_response_type(string responseType)
    {
        await SeedClient("c1", "https://cb.example/cb");
        var v = new AuthorizeRequestValidator(_store);
        var (ok, err) = await v.ValidateAsync(new AuthorizeRequest(
            responseType, "c1", "https://cb.example/cb", "challenge-43-chars-min-aaaaaaaaaaaaaaaaaaaaa",
            "S256", "s", "mcp"), default);
        ok.Should().BeFalse();
        err!.Code.Should().Be("unsupported_response_type");
    }

    [Fact]
    public async Task Rejects_unknown_client()
    {
        var v = new AuthorizeRequestValidator(_store);
        var (ok, err) = await v.ValidateAsync(new AuthorizeRequest(
            "code", "unknown", "https://cb.example/cb", "challenge-43-chars-min-aaaaaaaaaaaaaaaaaaaaa",
            "S256", "s", "mcp"), default);
        ok.Should().BeFalse();
        err!.Code.Should().Be("invalid_client");
    }

    [Fact]
    public async Task Rejects_redirect_uri_mismatch()
    {
        await SeedClient("c1", "https://cb.example/cb");
        var v = new AuthorizeRequestValidator(_store);
        var (ok, err) = await v.ValidateAsync(new AuthorizeRequest(
            "code", "c1", "https://evil.example/cb", "challenge-43-chars-min-aaaaaaaaaaaaaaaaaaaaa",
            "S256", "s", "mcp"), default);
        ok.Should().BeFalse();
        err!.Code.Should().Be("invalid_request");
    }

    [Fact]
    public async Task Rejects_non_S256_method()
    {
        await SeedClient("c1", "https://cb.example/cb");
        var v = new AuthorizeRequestValidator(_store);
        var (ok, err) = await v.ValidateAsync(new AuthorizeRequest(
            "code", "c1", "https://cb.example/cb", "challenge-43-chars-min-aaaaaaaaaaaaaaaaaaaaa",
            "plain", "s", "mcp"), default);
        ok.Should().BeFalse();
        err!.Code.Should().Be("invalid_request");
    }

    [Fact]
    public async Task Rejects_missing_state()
    {
        await SeedClient("c1", "https://cb.example/cb");
        var v = new AuthorizeRequestValidator(_store);
        var (ok, err) = await v.ValidateAsync(new AuthorizeRequest(
            "code", "c1", "https://cb.example/cb", "challenge-43-chars-min-aaaaaaaaaaaaaaaaaaaaa",
            "S256", "", "mcp"), default);
        ok.Should().BeFalse();
        err!.Code.Should().Be("invalid_request");
    }
}
