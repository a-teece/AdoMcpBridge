using System.Net;
using System.Text;
using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class AdoRestClientTests
{
    private const string CallerToken = "caller-delegated-ado-token";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public CapturingHandler(HttpResponseMessage response) => _response = response;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (AdoRestClient client, CapturingHandler handler) CreateClient(HttpResponseMessage response)
    {
        var handler = new CapturingHandler(response);
        var tokenProvider = Substitute.For<IAdoAccessTokenProvider>();
        tokenProvider.GetAccessToken().Returns(CallerToken);
        var client = new AdoRestClient(
            new HttpClient(handler), tokenProvider, NullLogger<AdoRestClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task GetFieldAsync_authenticates_with_the_callers_delegated_token()
    {
        var (client, handler) = CreateClient(Json("{\"fields\":{\"System.Title\":\"hi\"}}"));

        await client.GetFieldAsync("org", "proj", 42, "System.Title");

        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task GetFieldAsync_returns_the_stored_field_value()
    {
        var (client, _) = CreateClient(Json("{\"fields\":{\"System.Title\":\"the-value\"}}"));

        var value = await client.GetFieldAsync("org", "proj", 42, "System.Title");

        value.Should().Be("the-value");
    }

    [Fact]
    public async Task PatchFieldAsync_authenticates_with_the_callers_delegated_token()
    {
        var (client, handler) = CreateClient(Json("{}"));

        await client.PatchFieldAsync("org", "proj", 42, "System.Title", "new-value");

        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task GetWorkItemsBatchAsync_authenticates_with_the_callers_delegated_token()
    {
        var (client, handler) = CreateClient(Json("{\"value\":[]}"));

        await client.GetWorkItemsBatchAsync("org", "proj", [1, 2, 3]);

        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
    }
}
