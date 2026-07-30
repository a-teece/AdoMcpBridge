using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.CustomTools.Tools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class CommentProjectionTests
{
    private static JsonElement Comment(object shape) =>
        JsonDocument.Parse(JsonSerializer.Serialize(shape)).RootElement.Clone();

    private static JsonElement Project(JsonElement comment)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
            CommentProjection.WriteCommentMetadata(writer, comment);
        return JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray())).RootElement.Clone();
    }

    [Fact]
    public void Projects_metadata_without_the_full_body()
    {
        var comment = Comment(new
        {
            id = 12,
            text = "First line of the comment\nsecond line",
            createdBy = new { displayName = "Andrew Teece" },
            createdDate = "2026-07-01T10:00:00Z",
        });

        var projected = Project(comment);

        projected.GetProperty("id").GetInt32().Should().Be(12);
        projected.GetProperty("author").GetString().Should().Be("Andrew Teece");
        projected.GetProperty("createdDate").GetString().Should().Be("2026-07-01T10:00:00Z");
        projected.GetProperty("charCount").GetInt32().Should().Be("First line of the comment\nsecond line".Length);
        projected.GetProperty("preview").GetString().Should().Be("First line of the comment");
        projected.GetProperty("note").GetString().Should().Contain("ado_bridge_get_comment");
        projected.TryGetProperty("text", out _).Should().BeFalse();
    }

    [Fact]
    public void Preview_truncates_a_long_first_line_with_an_ellipsis()
    {
        var line = new string('a', CommentProjection.PreviewCharLimit + 50);

        var preview = CommentProjection.BuildPreview(line);

        preview.Length.Should().Be(CommentProjection.PreviewCharLimit + 1); // + ellipsis char
        preview.Should().EndWith("…");
    }

    [Fact]
    public void Preview_skips_blank_leading_lines_and_decodes_entities()
    {
        // AdoFieldEscaper.Unescape reverses ADO entity-encoding; &lt; becomes <.
        CommentProjection.BuildPreview("\n\n  &lt;b&gt;bold&lt;/b&gt; text  ")
            .Should().Be("<b>bold</b> text");
    }

    [Fact]
    public void Preview_of_empty_text_is_empty()
    {
        CommentProjection.BuildPreview("").Should().BeEmpty();
    }

    [Fact]
    public void Missing_optional_fields_do_not_throw()
    {
        var projected = Project(Comment(new { id = 3, text = "hi" }));

        projected.GetProperty("author").ValueKind.Should().Be(JsonValueKind.Null);
        projected.GetProperty("charCount").GetInt32().Should().Be(2);
    }
}
