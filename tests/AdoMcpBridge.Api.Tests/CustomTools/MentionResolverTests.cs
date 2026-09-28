using AdoMcpBridge.Api.CustomTools;
using FluentAssertions;

namespace AdoMcpBridge.Api.Tests.CustomTools;

public class MentionResolverTests
{
    // A fake resolver over a fixed email→identity map. Records how many times it was
    // called so tests can prove distinct-email caching and the no-token short circuit.
    private sealed class FakeResolver
    {
        private readonly Dictionary<string, AdoIdentity?> _map;
        public FakeResolver(Dictionary<string, AdoIdentity?> map) => _map = map;
        public int Calls { get; private set; }
        public List<string> ResolvedEmails { get; } = [];

        public Task<AdoIdentity?> ResolveAsync(string email, CancellationToken ct)
        {
            Calls++;
            ResolvedEmails.Add(email);
            return Task.FromResult(_map.TryGetValue(email, out var id) ? id : null);
        }
    }

    private static Task<string> Resolve(string body, bool markdown, FakeResolver fake) =>
        MentionResolver.ResolveAsync(body, markdown, fake.ResolveAsync, CancellationToken.None);

    [Fact]
    public async Task Markdown_single_mention_becomes_identity_id_token()
    {
        var fake = new FakeResolver(new() { ["ada@x.com"] = new AdoIdentity("guid-1", "Ada") });

        var result = await Resolve("hi @<ada@x.com> please review", markdown: true, fake);

        result.Should().Be("hi @<guid-1> please review");
    }

    [Fact]
    public async Task Html_single_mention_becomes_anchor_with_display_name()
    {
        var fake = new FakeResolver(new() { ["ada@x.com"] = new AdoIdentity("guid-1", "Ada Lovelace") });

        var result = await Resolve("hi @<ada@x.com>", markdown: false, fake);

        result.Should().Be(
            "hi <a href=\"#\" data-vss-mention=\"version:2.0,guid-1\">@Ada Lovelace</a>");
    }

    [Fact]
    public async Task Html_mention_html_encodes_the_display_name()
    {
        // A display name with HTML-significant characters must not break (or inject into)
        // the anchor markup of an HTML-format comment.
        var fake = new FakeResolver(new() { ["ada@x.com"] = new AdoIdentity("guid-1", "A & B <x> \"y\"") });

        var result = await Resolve("@<ada@x.com>", markdown: false, fake);

        result.Should().Be(
            "<a href=\"#\" data-vss-mention=\"version:2.0,guid-1\">@A &amp; B &lt;x&gt; &quot;y&quot;</a>");
    }

    [Fact]
    public async Task Multiple_distinct_mentions_are_each_resolved()
    {
        var fake = new FakeResolver(new()
        {
            ["ada@x.com"] = new AdoIdentity("guid-1", "Ada"),
            ["bob@x.com"] = new AdoIdentity("guid-2", "Bob"),
        });

        var result = await Resolve("@<ada@x.com> and @<bob@x.com>", markdown: true, fake);

        result.Should().Be("@<guid-1> and @<guid-2>");
        fake.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Repeated_same_email_is_resolved_once_but_replaced_everywhere()
    {
        var fake = new FakeResolver(new() { ["ada@x.com"] = new AdoIdentity("guid-1", "Ada") });

        var result = await Resolve("@<ada@x.com> and again @<ada@x.com>", markdown: true, fake);

        result.Should().Be("@<guid-1> and again @<guid-1>");
        fake.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Unresolved_email_is_left_as_the_original_token()
    {
        var fake = new FakeResolver(new() { ["ada@x.com"] = null });

        var result = await Resolve("ping @<ada@x.com>", markdown: true, fake);

        result.Should().Be("ping @<ada@x.com>");
    }

    [Fact]
    public async Task Body_with_no_tokens_is_unchanged_and_resolver_not_called()
    {
        var fake = new FakeResolver(new());

        var result = await Resolve("just a plain comment, no mentions", markdown: true, fake);

        result.Should().Be("just a plain comment, no mentions");
        fake.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Token_that_is_not_an_email_is_left_untouched_and_not_resolved()
    {
        var fake = new FakeResolver(new());

        var result = await Resolve("see @<not-an-email> here", markdown: true, fake);

        result.Should().Be("see @<not-an-email> here");
        fake.Calls.Should().Be(0);
    }
}
