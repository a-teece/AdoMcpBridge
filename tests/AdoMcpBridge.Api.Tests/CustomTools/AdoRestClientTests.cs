using System.Net;
using System.Text;
using System.Text.Json;
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
        public string? LastBody { get; private set; }
        public string? LastContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastContentType = request.Content.Headers.ContentType?.MediaType;
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return _response;
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

    // ── QueryApprovalsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task QueryApprovalsAsync_authenticates_with_the_callers_delegated_token()
    {
        var (client, handler) = CreateClient(Json("{\"count\":0,\"value\":[]}"));

        await client.QueryApprovalsAsync("org", "proj", null, null, null, null, null);

        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task QueryApprovalsAsync_builds_the_base_url_with_only_api_version_when_no_optional_params()
    {
        var (client, handler) = CreateClient(Json("{\"count\":0,\"value\":[]}"));

        await client.QueryApprovalsAsync("my org", "my proj", null, null, null, null, null);

        var url = handler.LastRequest!.RequestUri!.AbsoluteUri;
        url.Should().Be(
            "https://dev.azure.com/my%20org/my%20proj/_apis/pipelines/approvals?api-version=7.1");
    }

    [Fact]
    public async Task QueryApprovalsAsync_appends_all_optional_params_comma_joining_arrays()
    {
        var (client, handler) = CreateClient(Json("{\"count\":0,\"value\":[]}"));

        await client.QueryApprovalsAsync(
            "org", "proj",
            approvalIds: ["a1", "a2"],
            state: "pending",
            userIds: ["u1", "u2"],
            top: 25,
            expand: "steps");

        var url = handler.LastRequest!.RequestUri!.AbsoluteUri;
        url.Should().Contain("approvalIds=a1,a2");
        url.Should().Contain("expand=steps");
        url.Should().Contain("userIds=u1,u2");
        url.Should().Contain("state=pending");
        url.Should().Contain("top=25");
        url.Should().Contain("api-version=7.1");
    }

    [Fact]
    public async Task QueryApprovalsAsync_returns_the_value_array_elements()
    {
        var (client, _) = CreateClient(Json(
            "{\"count\":2,\"value\":[{\"id\":\"a1\"},{\"id\":\"a2\"}]}"));

        var approvals = await client.QueryApprovalsAsync("org", "proj", null, null, null, null, null);

        approvals.Should().HaveCount(2);
        approvals[0].GetProperty("id").GetString().Should().Be("a1");
        approvals[1].GetProperty("id").GetString().Should().Be("a2");
    }

    [Fact]
    public async Task QueryApprovalsAsync_returns_empty_when_no_value_property()
    {
        var (client, _) = CreateClient(Json("{\"count\":0}"));

        var approvals = await client.QueryApprovalsAsync("org", "proj", null, null, null, null, null);

        approvals.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryApprovalsAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(Json("bad", HttpStatusCode.BadRequest));

        var act = () => client.QueryApprovalsAsync("org", "proj", null, null, null, null, null);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── GetApprovalAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetApprovalAsync_authenticates_and_appends_expand_when_provided()
    {
        var (client, handler) = CreateClient(Json("{\"id\":\"a1\"}"));

        await client.GetApprovalAsync("org", "proj", "a1", "steps");

        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be(CallerToken);
        var url = handler.LastRequest.RequestUri!.AbsoluteUri;
        url.Should().Contain("/org/proj/_apis/pipelines/approvals/a1?");
        url.Should().Contain("expand=steps");
        url.Should().EndWith("api-version=7.1");
    }

    [Fact]
    public async Task GetApprovalAsync_omits_expand_when_not_provided()
    {
        var (client, handler) = CreateClient(Json("{\"id\":\"a1\"}"));

        await client.GetApprovalAsync("org", "proj", "a1", null);

        var url = handler.LastRequest!.RequestUri!.AbsoluteUri;
        url.Should().Be(
            "https://dev.azure.com/org/proj/_apis/pipelines/approvals/a1?api-version=7.1");
    }

    [Fact]
    public async Task GetApprovalAsync_returns_the_single_approval_object()
    {
        var (client, _) = CreateClient(Json("{\"id\":\"a1\",\"status\":\"pending\"}"));

        var approval = await client.GetApprovalAsync("org", "proj", "a1", null);

        approval.Should().NotBeNull();
        approval!.Value.GetProperty("status").GetString().Should().Be("pending");
    }

    [Fact]
    public async Task GetApprovalAsync_returns_null_on_404()
    {
        var (client, _) = CreateClient(Json("{}", HttpStatusCode.NotFound));

        var approval = await client.GetApprovalAsync("org", "proj", "missing", null);

        approval.Should().BeNull();
    }

    [Fact]
    public async Task GetApprovalAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(Json("bad", HttpStatusCode.InternalServerError));

        var act = () => client.GetApprovalAsync("org", "proj", "a1", null);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── UpdateApprovalsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateApprovalsAsync_patches_with_application_json_content_type()
    {
        var (client, handler) = CreateClient(Json("{\"count\":1,\"value\":[{\"id\":\"a1\"}]}"));

        await client.UpdateApprovalsAsync(
            "org", "proj", [new ApprovalUpdate("a1", "approved", "looks good")]);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        handler.LastContentType.Should().Be("application/json");
        var url = handler.LastRequest.RequestUri!.AbsoluteUri;
        url.Should().Be("https://dev.azure.com/org/proj/_apis/pipelines/approvals?api-version=7.1");
    }

    [Fact]
    public async Task UpdateApprovalsAsync_serializes_a_json_array_with_the_comment()
    {
        var (client, handler) = CreateClient(Json("{\"count\":1,\"value\":[{\"id\":\"a1\"}]}"));

        await client.UpdateApprovalsAsync(
            "org", "proj", [new ApprovalUpdate("a1", "approved", "looks good")]);

        var root = JsonDocument.Parse(handler.LastBody!).RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Array);
        root.GetArrayLength().Should().Be(1);
        root[0].GetProperty("approvalId").GetString().Should().Be("a1");
        root[0].GetProperty("status").GetString().Should().Be("approved");
        root[0].GetProperty("comment").GetString().Should().Be("looks good");
    }

    [Fact]
    public async Task UpdateApprovalsAsync_omits_null_comment_from_the_body()
    {
        var (client, handler) = CreateClient(Json("{\"count\":1,\"value\":[{\"id\":\"a1\"}]}"));

        await client.UpdateApprovalsAsync(
            "org", "proj", [new ApprovalUpdate("a1", "rejected", null)]);

        var root = JsonDocument.Parse(handler.LastBody!).RootElement;
        root[0].TryGetProperty("comment", out _).Should().BeFalse();
        root[0].GetProperty("status").GetString().Should().Be("rejected");
    }

    [Fact]
    public async Task UpdateApprovalsAsync_returns_the_value_array_elements()
    {
        var (client, _) = CreateClient(Json(
            "{\"count\":1,\"value\":[{\"id\":\"a1\",\"status\":\"approved\"}]}"));

        var updated = await client.UpdateApprovalsAsync(
            "org", "proj", [new ApprovalUpdate("a1", "approved", null)]);

        updated.Should().HaveCount(1);
        updated[0].GetProperty("status").GetString().Should().Be("approved");
    }

    [Fact]
    public async Task UpdateApprovalsAsync_authenticates_with_the_callers_delegated_token()
    {
        var (client, handler) = CreateClient(Json("{\"count\":1,\"value\":[{\"id\":\"a1\"}]}"));

        await client.UpdateApprovalsAsync(
            "org", "proj", [new ApprovalUpdate("a1", "approved", null)]);

        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task UpdateApprovalsAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(Json("bad", HttpStatusCode.Forbidden));

        var act = () => client.UpdateApprovalsAsync(
            "org", "proj", [new ApprovalUpdate("a1", "approved", null)]);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── work-item comments ───────────────────────────────────────────────────

    [Fact]
    public async Task GetWorkItemCommentsAsync_returns_the_comments_array_cloned()
    {
        var (client, handler) = CreateClient(Json(
            "{\"totalCount\":2,\"count\":2,\"comments\":[{\"id\":1,\"text\":\"a\"},{\"id\":2,\"text\":\"b\"}]}"));

        var comments = await client.GetWorkItemCommentsAsync("org", "proj", 42);

        comments.Should().HaveCount(2);
        comments[0].GetProperty("id").GetInt32().Should().Be(1);
        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/proj/_apis/wit/workItems/42/comments?api-version=7.1-preview.4");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task GetWorkItemCommentsAsync_returns_empty_when_no_comments_property()
    {
        var (client, _) = CreateClient(Json("{\"totalCount\":0,\"count\":0}"));

        var comments = await client.GetWorkItemCommentsAsync("org", "proj", 42);

        comments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorkItemCommentsAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(Json("nope", HttpStatusCode.Unauthorized));

        var act = () => client.GetWorkItemCommentsAsync("org", "proj", 42);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetWorkItemCommentAsync_returns_the_single_comment()
    {
        var (client, handler) = CreateClient(Json("{\"id\":7,\"text\":\"hello\"}"));

        var comment = await client.GetWorkItemCommentAsync("org", "proj", 42, 7);

        comment.Should().NotBeNull();
        comment!.Value.GetProperty("text").GetString().Should().Be("hello");
        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/proj/_apis/wit/workItems/42/comments/7?api-version=7.1-preview.4");
    }

    [Fact]
    public async Task GetWorkItemCommentAsync_returns_null_on_404()
    {
        var (client, _) = CreateClient(Json("{}", HttpStatusCode.NotFound));

        var comment = await client.GetWorkItemCommentAsync("org", "proj", 42, 7);

        comment.Should().BeNull();
    }

    [Fact]
    public async Task AddWorkItemCommentAsync_posts_the_text_and_returns_created_comment()
    {
        var (client, handler) = CreateClient(Json("{\"id\":99,\"text\":\"posted\"}"));

        var created = await client.AddWorkItemCommentAsync("org", "proj", 42, "posted");

        created.GetProperty("id").GetInt32().Should().Be(99);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/proj/_apis/wit/workItems/42/comments?api-version=7.1-preview.4");
        using var sent = JsonDocument.Parse(handler.LastBody!);
        sent.RootElement.GetProperty("text").GetString().Should().Be("posted");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task AddWorkItemCommentAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(Json("bad", HttpStatusCode.BadRequest));

        var act = () => client.AddWorkItemCommentAsync("org", "proj", 42, "x");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
