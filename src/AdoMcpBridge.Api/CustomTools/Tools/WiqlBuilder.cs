using System.Text;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>Structured filter for <see cref="WiqlBuilder.Build"/>; null or empty members are ignored.</summary>
internal sealed record WiqlFilter(
    string Project,
    IReadOnlyList<string>? Types,
    IReadOnlyList<string>? States,
    string? AssignedTo,
    string? AreaPath,
    string? IterationPath,
    IReadOnlyList<string>? Tags,
    string? TextContains,
    string OrderBy);

/// <summary>
/// Builds flat WIQL from a <see cref="WiqlFilter"/>. Injection-safe by construction: every
/// user-supplied value goes through <see cref="Quote"/>, and field names (including the ORDER BY
/// field) come only from fixed constants or <see cref="OrderByFields"/> — never from input text.
/// </summary>
internal static class WiqlBuilder
{
    /// <summary>The fields <c>orderBy</c> may name (case-insensitive), in canonical spelling.</summary>
    internal static readonly IReadOnlyList<string> OrderByFields =
    [
        "System.ChangedDate",
        "System.CreatedDate",
        "System.Id",
        "Microsoft.VSTS.Common.StackRank",
        "Microsoft.VSTS.Common.Priority",
    ];

    private const string Me = "@me";

    public static string Build(WiqlFilter f)
    {
        var orderBy = ParseOrderBy(f.OrderBy);

        var sb = new StringBuilder("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = ")
            .Append(Quote(f.Project));

        AppendIn(sb, "System.WorkItemType", f.Types);
        AppendIn(sb, "System.State", f.States);

        if (f.AssignedTo is { } assignedTo)
            sb.Append(" AND [System.AssignedTo] = ")
                .Append(string.Equals(assignedTo, Me, StringComparison.OrdinalIgnoreCase) ? "@Me" : Quote(assignedTo));

        if (f.AreaPath is { } areaPath)
            sb.Append(" AND [System.AreaPath] UNDER ").Append(Quote(areaPath));
        if (f.IterationPath is { } iterationPath)
            sb.Append(" AND [System.IterationPath] UNDER ").Append(Quote(iterationPath));

        foreach (var tag in f.Tags ?? [])
            sb.Append(" AND [System.Tags] CONTAINS ").Append(Quote(tag));

        if (f.TextContains is { } text)
            sb.Append(" AND [System.Title] CONTAINS ").Append(Quote(text));

        return sb.Append(" ORDER BY ").Append(orderBy).ToString();
    }

    /// <summary>Renders <paramref name="value"/> as a WIQL string literal, doubling embedded single quotes.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static void AppendIn(StringBuilder sb, string field, IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
            return;

        sb.Append(" AND [").Append(field).Append("] IN (")
            .AppendJoin(", ", values.Select(Quote))
            .Append(')');
    }

    private static string ParseOrderBy(string orderBy)
    {
        var parts = orderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var field = parts.Length is 1 or 2
            ? OrderByFields.FirstOrDefault(f => string.Equals(f, parts[0], StringComparison.OrdinalIgnoreCase))
            : null;
        var direction = parts.Length == 1 ? "ASC" : parts.Length == 2 ? Direction(parts[1]) : null;

        if (field is null || direction is null)
            throw new CallerArgumentException(
                $"'orderBy' must be one of {string.Join(", ", OrderByFields)}, " +
                "optionally followed by 'asc' or 'desc'.");

        return $"[{field}] {direction}";
    }

    private static string? Direction(string value)
        => string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC"
            : string.Equals(value, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC"
            : null;
}
