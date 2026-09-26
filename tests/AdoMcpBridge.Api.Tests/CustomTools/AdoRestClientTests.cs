using System.Net;
using System.Net.Http.Headers;
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

        await act.Should().ThrowAsync<AdoRestException>();
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

        await act.Should().ThrowAsync<AdoRestException>();
    }

    [Fact]
    public async Task GetApprovalAsync_surfaces_the_parsed_ado_message_and_status_on_non_success()
    {
        var (client, _) = CreateClient(Json(
            "{\"message\":\"TF401019: access denied\"}", HttpStatusCode.Forbidden));

        var act = () => client.GetApprovalAsync("org", "proj", "a1", null);

        var ex = (await act.Should().ThrowAsync<AdoRestException>()).Which;
        ex.Message.Should().Be("TF401019: access denied");
        ex.StatusCode.Should().Be(403);
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

        await act.Should().ThrowAsync<AdoRestException>();
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

        await act.Should().ThrowAsync<AdoRestException>();
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
    public async Task AddWorkItemCommentAsync_posts_markdown_with_format_0_and_returns_created_comment()
    {
        var (client, handler) = CreateClient(Json("{\"id\":99,\"text\":\"posted\"}"));

        var created = await client.AddWorkItemCommentAsync("org", "proj", 42, "posted", markdown: true);

        created.GetProperty("id").GetInt32().Should().Be(99);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/proj/_apis/wit/workItems/42/comments?format=0&api-version=7.2-preview.4");
        using var sent = JsonDocument.Parse(handler.LastBody!);
        sent.RootElement.GetProperty("text").GetString().Should().Be("posted");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task AddWorkItemCommentAsync_posts_html_with_format_1()
    {
        var (client, handler) = CreateClient(Json("{\"id\":99}"));

        await client.AddWorkItemCommentAsync("org", "proj", 42, "<b>posted</b>", markdown: false);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/proj/_apis/wit/workItems/42/comments?format=1&api-version=7.2-preview.4");
        using var sent = JsonDocument.Parse(handler.LastBody!);
        sent.RootElement.GetProperty("text").GetString().Should().Be("<b>posted</b>");
    }

    [Fact]
    public async Task AddWorkItemCommentAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(Json("bad", HttpStatusCode.BadRequest));

        var act = () => client.AddWorkItemCommentAsync("org", "proj", 42, "x", markdown: true);

        await act.Should().ThrowAsync<AdoRestException>();
    }

    [Fact]
    public async Task UpdateWorkItemCommentAsync_patches_markdown_with_format_0_and_returns_updated_comment()
    {
        var (client, handler) = CreateClient(Json("{\"id\":7,\"text\":\"edited\"}"));

        var updated = await client.UpdateWorkItemCommentAsync("org", "proj", 42, 7, "edited", markdown: true);

        updated.GetProperty("id").GetInt32().Should().Be(7);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/proj/_apis/wit/workItems/42/comments/7?format=0&api-version=7.2-preview.4");
        using var sent = JsonDocument.Parse(handler.LastBody!);
        sent.RootElement.GetProperty("text").GetString().Should().Be("edited");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task UpdateWorkItemCommentAsync_patches_html_with_format_1()
    {
        var (client, handler) = CreateClient(Json("{\"id\":7}"));

        await client.UpdateWorkItemCommentAsync("org", "proj", 42, 7, "<b>edited</b>", markdown: false);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/proj/_apis/wit/workItems/42/comments/7?format=1&api-version=7.2-preview.4");
        using var sent = JsonDocument.Parse(handler.LastBody!);
        sent.RootElement.GetProperty("text").GetString().Should().Be("<b>edited</b>");
    }

    [Fact]
    public async Task UpdateWorkItemCommentAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(Json("bad", HttpStatusCode.BadRequest));

        var act = () => client.UpdateWorkItemCommentAsync("org", "proj", 42, 7, "x", markdown: true);

        await act.Should().ThrowAsync<AdoRestException>();
    }

    // ── QueryByWiqlAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task QueryByWiqlAsync_authenticates_with_the_callers_delegated_token()
    {
        var (client, handler) = CreateClient(Json("{\"queryType\":\"flat\",\"workItems\":[]}"));

        await client.QueryByWiqlAsync("org", null, null, "q", null, null);

        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
    }

    [Fact]
    public async Task QueryByWiqlAsync_builds_the_org_scoped_url()
    {
        var (client, handler) = CreateClient(Json("{\"workItems\":[]}"));

        await client.QueryByWiqlAsync("my org", null, null, "q", null, null);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/my%20org/_apis/wit/wiql?api-version=7.1");
    }

    [Fact]
    public async Task QueryByWiqlAsync_builds_the_project_scoped_url_with_escaped_segments()
    {
        var (client, handler) = CreateClient(Json("{\"workItems\":[]}"));

        await client.QueryByWiqlAsync("org", "my proj", null, "q", null, null);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/my%20proj/_apis/wit/wiql?api-version=7.1");
    }

    [Fact]
    public async Task QueryByWiqlAsync_builds_the_team_scoped_url_with_escaped_segments()
    {
        var (client, handler) = CreateClient(Json("{\"workItems\":[]}"));

        await client.QueryByWiqlAsync("org", "proj", "my team", "q", null, null);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://dev.azure.com/org/proj/my%20team/_apis/wit/wiql?api-version=7.1");
    }

    [Fact]
    public async Task QueryByWiqlAsync_appends_top_and_timePrecision_query_params()
    {
        var (client, handler) = CreateClient(Json("{\"workItems\":[]}"));

        await client.QueryByWiqlAsync("org", null, null, "q", 51, true);

        var url = handler.LastRequest!.RequestUri!.AbsoluteUri;
        url.Should().Contain("%24top=51");
        url.Should().Contain("timePrecision=true");
    }

    [Fact]
    public async Task QueryByWiqlAsync_writes_false_timePrecision_when_disabled()
    {
        var (client, handler) = CreateClient(Json("{\"workItems\":[]}"));

        await client.QueryByWiqlAsync("org", null, null, "q", null, false);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Contain("timePrecision=false");
    }

    [Fact]
    public async Task QueryByWiqlAsync_posts_the_query_text_as_the_json_body()
    {
        var (client, handler) = CreateClient(Json("{\"workItems\":[]}"));

        await client.QueryByWiqlAsync("org", null, null, "SELECT [System.Id] FROM WorkItems", null, null);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastContentType.Should().Be("application/json");
        var root = JsonDocument.Parse(handler.LastBody!).RootElement;
        root.GetProperty("query").GetString().Should().Be("SELECT [System.Id] FROM WorkItems");
    }

    [Fact]
    public async Task QueryByWiqlAsync_returns_the_cloned_result_element()
    {
        var (client, _) = CreateClient(Json(
            "{\"queryType\":\"flat\",\"workItems\":[{\"id\":101}]}"));

        var result = await client.QueryByWiqlAsync("org", null, null, "q", null, null);

        result.GetProperty("queryType").GetString().Should().Be("flat");
        result.GetProperty("workItems")[0].GetProperty("id").GetInt32().Should().Be(101);
    }

    [Fact]
    public async Task QueryByWiqlAsync_surfaces_the_ado_message_on_400()
    {
        var (client, _) = CreateClient(Json(
            "{\"message\":\"TF51005: bad field [System.Bogus]\"}", HttpStatusCode.BadRequest));

        var act = () => client.QueryByWiqlAsync("org", null, null, "q", null, null);

        var ex = (await act.Should().ThrowAsync<AdoRestException>()).Which;
        ex.Message.Should().Be("TF51005: bad field [System.Bogus]");
        ex.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task QueryByWiqlAsync_falls_back_to_raw_body_when_not_json()
    {
        var (client, _) = CreateClient(Json("plain-text failure", HttpStatusCode.BadRequest));

        var act = () => client.QueryByWiqlAsync("org", null, null, "q", null, null);

        (await act.Should().ThrowAsync<AdoRestException>())
            .Which.Message.Should().Be("plain-text failure");
    }

    [Fact]
    public async Task QueryByWiqlAsync_falls_back_to_raw_body_when_json_lacks_message()
    {
        var (client, _) = CreateClient(Json("{\"typeKey\":\"X\"}", HttpStatusCode.BadRequest));

        var act = () => client.QueryByWiqlAsync("org", null, null, "q", null, null);

        (await act.Should().ThrowAsync<AdoRestException>())
            .Which.Message.Should().Be("{\"typeKey\":\"X\"}");
    }

    [Fact]
    public async Task QueryByWiqlAsync_falls_back_to_raw_body_when_message_is_not_a_string()
    {
        var (client, _) = CreateClient(Json("{\"message\":123}", HttpStatusCode.BadRequest));

        var act = () => client.QueryByWiqlAsync("org", null, null, "q", null, null);

        (await act.Should().ThrowAsync<AdoRestException>())
            .Which.Message.Should().Be("{\"message\":123}");
    }

    [Fact]
    public async Task QueryByWiqlAsync_falls_back_to_raw_body_when_root_is_not_an_object()
    {
        var (client, _) = CreateClient(Json("[\"unexpected\"]", HttpStatusCode.BadRequest));

        var act = () => client.QueryByWiqlAsync("org", null, null, "q", null, null);

        (await act.Should().ThrowAsync<AdoRestException>())
            .Which.Message.Should().Be("[\"unexpected\"]");
    }

    [Fact]
    public async Task QueryByWiqlAsync_falls_back_to_status_when_body_is_empty()
    {
        var (client, _) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json"),
            });

        var act = () => client.QueryByWiqlAsync("org", null, null, "q", null, null);

        (await act.Should().ThrowAsync<AdoRestException>())
            .Which.Message.Should().Contain("500");
    }

    // ── DownloadAttachmentAsync ──────────────────────────────────────────────

    private static HttpResponseMessage Binary(byte[] bytes, string? contentType)
    {
        var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        if (contentType is not null)
            res.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return res;
    }

    [Fact]
    public async Task DownloadAttachmentAsync_gets_by_id_with_download_flag_and_filename()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var (client, handler) = CreateClient(Binary(bytes, "image/png"));

        var result = await client.DownloadAttachmentAsync("org", "proj", "the-guid", "pic.png");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
        handler.LastRequest.RequestUri!.ToString()
            .Should().Contain("/proj/_apis/wit/attachments/the-guid")
            .And.Contain("download=true")
            .And.Contain("fileName=pic.png");
        result.Content.Should().Equal(bytes);
        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task DownloadAttachmentAsync_omits_filename_when_not_supplied()
    {
        var (client, handler) = CreateClient(Binary([9], "text/plain"));

        await client.DownloadAttachmentAsync("org", "proj", "the-guid");

        handler.LastRequest!.RequestUri!.ToString().Should().NotContain("fileName=");
    }

    [Fact]
    public async Task DownloadAttachmentAsync_defaults_content_type_when_absent()
    {
        var (client, _) = CreateClient(Binary([9], contentType: null));

        var result = await client.DownloadAttachmentAsync("org", "proj", "the-guid");

        result.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task DownloadAttachmentAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") });

        var act = () => client.DownloadAttachmentAsync("org", "proj", "the-guid");

        await act.Should().ThrowAsync<AdoRestException>();
    }

    // ── GetRepoItemContentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetRepoItemContentAsync_gets_by_path_with_download_flag_and_returns_bytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var (client, handler) = CreateClient(Binary(bytes, "text/plain"));

        var result = await client.GetRepoItemContentAsync(
            "org", "proj", "my-repo", "/src/Foo.cs", null, null);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
        var url = handler.LastRequest.RequestUri!.ToString();
        url.Should().Contain("/proj/_apis/git/repositories/my-repo/items")
            .And.Contain("path=%2Fsrc%2FFoo.cs")
            .And.Contain("download=true")
            .And.Contain("api-version=7.1");
        url.Should().NotContain("versionDescriptor");
        result.Should().NotBeNull();
        result!.Content.Should().Equal(bytes);
        result.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task GetRepoItemContentAsync_appends_version_descriptor_with_supplied_type()
    {
        var (client, handler) = CreateClient(Binary([9], "text/plain"));

        await client.GetRepoItemContentAsync(
            "org", "proj", "my-repo", "/README.md", "abc123", "Commit");

        var url = handler.LastRequest!.RequestUri!.ToString();
        url.Should().Contain("versionDescriptor.version=abc123")
            .And.Contain("versionDescriptor.versionType=Commit");
    }

    [Fact]
    public async Task GetRepoItemContentAsync_defaults_version_type_to_branch_when_version_given_without_type()
    {
        var (client, handler) = CreateClient(Binary([9], "text/plain"));

        await client.GetRepoItemContentAsync(
            "org", "proj", "my-repo", "/README.md", "main", null);

        var url = handler.LastRequest!.RequestUri!.ToString();
        url.Should().Contain("versionDescriptor.version=main")
            .And.Contain("versionDescriptor.versionType=Branch");
    }

    [Fact]
    public async Task GetRepoItemContentAsync_defaults_content_type_when_absent()
    {
        var (client, _) = CreateClient(Binary([9], contentType: null));

        var result = await client.GetRepoItemContentAsync(
            "org", "proj", "my-repo", "/README.md", null, null);

        result!.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task GetRepoItemContentAsync_returns_null_on_404()
    {
        var (client, _) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("no file") });

        var result = await client.GetRepoItemContentAsync(
            "org", "proj", "my-repo", "/missing.cs", null, null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRepoItemContentAsync_throws_with_status_and_message_on_non_success()
    {
        var (client, _) = CreateClient(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                "{\"message\":\"access denied\"}", Encoding.UTF8, "application/json"),
        });

        var act = () => client.GetRepoItemContentAsync("org", "proj", "my-repo", "/secret.cs", null, null);

        var ex = (await act.Should().ThrowAsync<AdoRestException>()).Which;
        ex.StatusCode.Should().Be(403);
        ex.Message.Should().Be("access denied");
    }

    // ── CreateAttachmentAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CreateAttachmentAsync_posts_octet_stream_and_returns_the_ref()
    {
        var (client, handler) = CreateClient(Json(
            "{\"id\":\"a5cedde4-2dd5-4fcf-befe-fd0977dd3433\"," +
            "\"url\":\"https://dev.azure.com/fabrikam/_apis/wit/attachments/a5cedde4-2dd5-4fcf-befe-fd0977dd3433?fileName=pic.png\"}",
            HttpStatusCode.Created));

        var result = await client.CreateAttachmentAsync("org", "proj", "pic.png", [1, 2, 3]);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
        handler.LastRequest.RequestUri!.ToString()
            .Should().Contain("/proj/_apis/wit/attachments")
            .And.Contain("fileName=pic.png");
        handler.LastContentType.Should().Be("application/octet-stream");
        result.Id.Should().Be("a5cedde4-2dd5-4fcf-befe-fd0977dd3433");
        result.Url.Should().Contain("/_apis/wit/attachments/a5cedde4-2dd5-4fcf-befe-fd0977dd3433");
    }

    [Fact]
    public async Task CreateAttachmentAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") });

        var act = () => client.CreateAttachmentAsync("org", "proj", "f", [1]);

        await act.Should().ThrowAsync<AdoRestException>();
    }

    // ── AddWorkItemAttachmentAsync ───────────────────────────────────────────

    [Fact]
    public async Task AddWorkItemAttachmentAsync_patches_an_attached_file_relation_with_comment()
    {
        var (client, handler) = CreateClient(Json("{}"));

        await client.AddWorkItemAttachmentAsync(
            "org", "proj", 42, "https://dev.azure.com/org/_apis/wit/attachments/g", "see attached");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        handler.LastContentType.Should().Be("application/json-patch+json");
        handler.LastRequest.RequestUri!.ToString().Should().Contain("/proj/_apis/wit/workitems/42");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var op = doc.RootElement[0];
        op.GetProperty("op").GetString().Should().Be("add");
        op.GetProperty("path").GetString().Should().Be("/relations/-");
        var value = op.GetProperty("value");
        value.GetProperty("rel").GetString().Should().Be("AttachedFile");
        value.GetProperty("url").GetString().Should().Be("https://dev.azure.com/org/_apis/wit/attachments/g");
        value.GetProperty("attributes").GetProperty("comment").GetString().Should().Be("see attached");
    }

    [Fact]
    public async Task AddWorkItemAttachmentAsync_omits_comment_when_not_supplied()
    {
        var (client, handler) = CreateClient(Json("{}"));

        await client.AddWorkItemAttachmentAsync("org", "proj", 42, "https://x/attachments/g", comment: null);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var attributes = doc.RootElement[0].GetProperty("value").GetProperty("attributes");
        attributes.TryGetProperty("comment", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AddWorkItemAttachmentAsync_throws_on_non_success()
    {
        var (client, _) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("no wi") });

        var act = () => client.AddWorkItemAttachmentAsync("org", "proj", 42, "https://x/a/g", null);

        await act.Should().ThrowAsync<AdoRestException>();
    }

    // ── UpdatePullRequestDescriptionAsync ────────────────────────────────────

    [Fact]
    public async Task UpdatePullRequestDescriptionAsync_patches_the_pull_request_url_with_the_description()
    {
        var (client, handler) = CreateClient(Json("{}"));

        await client.UpdatePullRequestDescriptionAsync("my org", "my proj", "my repo", 77, "the description");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
        handler.LastRequest.RequestUri!.AbsoluteUri.Should().Be(
            "https://dev.azure.com/my%20org/my%20proj/_apis/git/repositories/my%20repo/pullRequests/77?api-version=7.1");
        handler.LastContentType.Should().Be("application/json");

        using var doc = JsonDocument.Parse(handler.LastBody!);
        doc.RootElement.GetProperty("description").GetString().Should().Be("the description");
    }

    [Fact]
    public async Task UpdatePullRequestDescriptionAsync_throws_with_status_and_message_on_non_success()
    {
        var (client, _) = CreateClient(
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"pull request does not exist\"}"),
            });

        var act = () => client.UpdatePullRequestDescriptionAsync("org", "proj", "repo", 999, "x");

        var ex = (await act.Should().ThrowAsync<AdoRestException>()).Which;
        ex.StatusCode.Should().Be(404);
        ex.Message.Should().Be("pull request does not exist");
    }

    // ── CreateWorkItemAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkItemAsync_posts_json_patch_to_the_dollar_prefixed_type_url()
    {
        var (client, handler) = CreateClient(Json("{\"id\":123}", HttpStatusCode.OK));

        await client.CreateWorkItemAsync(
            "org", "proj", "Bug",
            [new { op = "add", path = "/fields/System.Title", value = "T" }]);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(CallerToken);
        handler.LastRequest.RequestUri!.AbsoluteUri.Should().Be(
            "https://dev.azure.com/org/proj/_apis/wit/workitems/$Bug?api-version=7.1");
        handler.LastContentType.Should().Be("application/json-patch+json");
    }

    [Fact]
    public async Task CreateWorkItemAsync_escapes_org_project_and_type_but_keeps_the_literal_dollar()
    {
        var (client, handler) = CreateClient(Json("{\"id\":1}"));

        await client.CreateWorkItemAsync("my org", "my proj", "User Story", []);

        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Be(
            "https://dev.azure.com/my%20org/my%20proj/_apis/wit/workitems/$User%20Story?api-version=7.1");
    }

    [Fact]
    public async Task CreateWorkItemAsync_serializes_the_patch_ops_as_the_body()
    {
        var (client, handler) = CreateClient(Json("{\"id\":1}"));

        await client.CreateWorkItemAsync(
            "org", "proj", "Task",
            [
                new { op = "add", path = "/fields/System.Title", value = "hello" },
                new { op = "add", path = "/multilineFieldsFormat/System.Description", value = "Markdown" },
            ]);

        var root = JsonDocument.Parse(handler.LastBody!).RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Array);
        root.GetArrayLength().Should().Be(2);
        root[0].GetProperty("op").GetString().Should().Be("add");
        root[0].GetProperty("path").GetString().Should().Be("/fields/System.Title");
        root[0].GetProperty("value").GetString().Should().Be("hello");
        root[1].GetProperty("path").GetString().Should().Be("/multilineFieldsFormat/System.Description");
    }

    [Fact]
    public async Task CreateWorkItemAsync_returns_the_created_work_item_element()
    {
        var (client, _) = CreateClient(Json("{\"id\":456,\"fields\":{\"System.Title\":\"T\"}}"));

        var created = await client.CreateWorkItemAsync("org", "proj", "Bug", []);

        created.GetProperty("id").GetInt32().Should().Be(456);
        created.GetProperty("fields").GetProperty("System.Title").GetString().Should().Be("T");
    }

    [Fact]
    public async Task CreateWorkItemAsync_throws_with_status_and_message_on_non_success()
    {
        var (client, _) = CreateClient(Json(
            "{\"message\":\"The field 'System.Title' is required.\"}", HttpStatusCode.BadRequest));

        var act = () => client.CreateWorkItemAsync("org", "proj", "Bug", []);

        var ex = (await act.Should().ThrowAsync<AdoRestException>()).Which;
        ex.StatusCode.Should().Be(400);
        ex.Message.Should().Be("The field 'System.Title' is required.");
    }
}
