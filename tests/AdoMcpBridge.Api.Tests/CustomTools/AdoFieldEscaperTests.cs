using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class AdoFieldEscaperTests
{
    // ── Escape ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a & b < c > d", "a &amp; b &lt; c &gt; d")]
    [InlineData("no special chars", "no special chars")]
    [InlineData("", "")]
    [InlineData("x & y", "x &amp; y")]
    [InlineData("<T>", "&lt;T&gt;")]
    [InlineData("feature/<feature-id>-<slug>", "feature/&lt;feature-id&gt;-&lt;slug&gt;")]
    public void Escape_ConvertsReservedCharacters(string input, string expected) =>
        AdoFieldEscaper.Escape(input).Should().Be(expected);

    [Fact]
    public void Escape_ProcessesAmpersandBeforeAngleBrackets_SoEntitySequencesAreNotDoubleEscaped()
    {
        // If & were escaped after < we'd get &amp;lt; → &amp;amp;lt; (wrong).
        // Correct: & → &amp; first, so &amp; in source becomes &amp;amp;.
        AdoFieldEscaper.Escape("&amp;")
            .Should().Be("&amp;amp;");
    }

    [Fact]
    public void Escape_DoesNotEscapeDoubleQuotes()
    {
        // ADO independently encodes " on ingest; escape must not pre-encode
        // it or the & in &quot; would get double-escaped on the next call.
        AdoFieldEscaper.Escape("say \"hello\"")
            .Should().Be("say \"hello\"");
    }

    // ── Unescape ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a &amp; b &lt; c &gt; d", "a & b < c > d")]
    [InlineData("no entities", "no entities")]
    [InlineData("", "")]
    public void Unescape_ReversesEscapedEntities(string input, string expected) =>
        AdoFieldEscaper.Unescape(input).Should().Be(expected);

    [Fact]
    public void Unescape_ReversesAdoIndependentQuoteEncoding()
    {
        // ADO's own sanitiser HTML-encodes literal " as &quot; on ingest,
        // regardless of what escape() sent. Unescape must reverse it even
        // though Escape never produced it.
        AdoFieldEscaper.Unescape("She said &quot;hello&quot; to the room.")
            .Should().Be("She said \"hello\" to the room.");
    }

    [Fact]
    public void Unescape_ProcessesAmpersandLast_SoLiteralAmpLtInSourceSurvivesRoundTrip()
    {
        // A literal "&lt;" in the source becomes "&amp;lt;" after Escape.
        // If &amp; were unescaped first we'd get "&lt;" which would then be
        // unescaped to "<" — wrong. &amp; must be last.
        var original = "List<T> and the literal string \"&lt;\" both appear here.";
        AdoFieldEscaper.Unescape(AdoFieldEscaper.Escape(original))
            .Should().Be(original);
    }

    [Fact]
    public void Unescape_LiteralAmpQuotSubstringSurvivesRoundTrip()
    {
        // A literal "&quot;" in the source (not a real quote entity) must
        // survive escape → unescape intact. Escape turns & → &amp;, giving
        // "&amp;quot;" which unescapes back to "&quot;" correctly.
        var original = "The literal string &quot; appears here, not a real quote.";
        AdoFieldEscaper.Unescape(AdoFieldEscaper.Escape(original))
            .Should().Be(original);
    }

    // ── NormalizeTrailingNewlines ────────────────────────────────────────────

    [Theory]
    [InlineData("hello\n", "hello")]
    [InlineData("hello\n\n", "hello")]
    [InlineData("hello\r\n", "hello")]
    [InlineData("hello\r\n\r\n", "hello")]
    [InlineData("hello", "hello")]
    [InlineData("", "")]
    [InlineData("mid\nnewline\nlast\n", "mid\nnewline\nlast")]
    public void NormalizeTrailingNewlines_StripsTrailingLineEndings(string input, string expected) =>
        AdoFieldEscaper.NormalizeTrailingNewlines(input).Should().Be(expected);

    [Fact]
    public void NormalizeTrailingNewlines_PreservesInternalNewlines()
    {
        AdoFieldEscaper.NormalizeTrailingNewlines("a\nb\nc\n")
            .Should().Be("a\nb\nc");
    }

    // ── Verify ──────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_MatchWhenEscapedContentRoundTrips()
    {
        var original = "Plan step: if x < y emit A & B.";
        var fromAdo = AdoFieldEscaper.Escape(original);
        var (isMatch, charCount) = AdoFieldEscaper.Verify(original, fromAdo);
        isMatch.Should().BeTrue();
        charCount.Should().Be(original.Length);
    }

    [Fact]
    public void Verify_MatchIgnoresTrailingNewlineDifference()
    {
        var original = "same content";
        var fromAdo = AdoFieldEscaper.Escape("same content") + "\n\n";
        var (isMatch, _) = AdoFieldEscaper.Verify(original, fromAdo);
        isMatch.Should().BeTrue();
    }

    [Fact]
    public void Verify_MatchThroughAdoIndependentQuoteEncoding()
    {
        // Exact live-ADO shape: our &/</> escaping plus ADO's own &quot; layered on top.
        var original = "Config: {\"key\": \"value\"} & done.";
        var sent = AdoFieldEscaper.Escape(original);
        // Simulate ADO independently encoding " → &quot; in what it stores.
        var fromAdo = sent.Replace("\"", "&quot;");
        var (isMatch, _) = AdoFieldEscaper.Verify(original, fromAdo);
        isMatch.Should().BeTrue();
    }

    [Fact]
    public void Verify_MismatchWhenContentDiffers()
    {
        var original = "correct content";
        var fromAdo = AdoFieldEscaper.Escape("different content");
        var (isMatch, _) = AdoFieldEscaper.Verify(original, fromAdo);
        isMatch.Should().BeFalse();
    }

    [Fact]
    public void Verify_CharCountIsLengthOfNormalisedOriginal()
    {
        var original = "hello";
        var fromAdo = AdoFieldEscaper.Escape(original);
        var (_, charCount) = AdoFieldEscaper.Verify(original, fromAdo);
        charCount.Should().Be(5);
    }
}
