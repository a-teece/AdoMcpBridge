using System.Text.RegularExpressions;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Rewrites <c>@&lt;email@domain.com&gt;</c> tokens in a comment body into real Azure DevOps
/// mentions before the body is posted. A markdown comment mentions by identity id
/// (<c>@&lt;{id}&gt;</c>); an HTML comment mentions with a <c>data-vss-mention</c> anchor. The
/// mechanism is Microsoft-documented (learn.microsoft.com/azure/devops/organizations/notifications/at-mentions).
/// Pure given the <c>resolve</c> delegate, so it is unit-testable without a network.
/// </summary>
internal static partial class MentionResolver
{
    // Matches @<email> where email is a run without whitespace, angle brackets, or '@',
    // separated by a single '@'. Deliberately loose — ADO decides what actually resolves.
    [GeneratedRegex(@"@<([^<>@\s]+@[^<>@\s]+)>")]
    private static partial Regex MentionToken();

    /// <summary>
    /// Replaces every <c>@&lt;email&gt;</c> token whose email resolves via <paramref name="resolve"/>.
    /// Each distinct email is resolved at most once. Unresolved tokens (resolve returns
    /// <see langword="null"/>) are left exactly as written. A body with no tokens is returned
    /// unchanged and <paramref name="resolve"/> is never called.
    /// </summary>
    public static async Task<string> ResolveAsync(
        string body,
        bool markdown,
        Func<string, CancellationToken, Task<AdoIdentity?>> resolve,
        CancellationToken ct)
    {
        var matches = MentionToken().Matches(body);
        if (matches.Count == 0) return body;

        var resolved = new Dictionary<string, AdoIdentity?>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            var email = match.Groups[1].Value;
            if (!resolved.ContainsKey(email))
                resolved[email] = await resolve(email, ct).ConfigureAwait(false);
        }

        return MentionToken().Replace(body, match =>
        {
            var email = match.Groups[1].Value;
            var identity = resolved[email];
            if (identity is null) return match.Value; // unresolved — leave the token untouched.

            return markdown
                ? $"@<{identity.Id}>"
                : $"<a href=\"#\" data-vss-mention=\"version:2.0,{identity.Id}\">@{System.Net.WebUtility.HtmlEncode(identity.DisplayName)}</a>";
        });
    }
}
