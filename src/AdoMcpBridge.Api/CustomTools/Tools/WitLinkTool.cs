using System.Globalization;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Adds or removes one work-item-to-work-item link (parent/child, related, dependency,
/// duplicate). Add appends to <c>/relations/-</c>; remove reads the item's relations, finds the
/// entry matching both the relation type and the target id, and removes it by index — guarded by
/// a JSON-Patch <c>test</c> on <c>/rev</c> so a concurrent edit that shifted the relations makes
/// ADO reject the patch instead of removing the wrong link.
/// </summary>
internal sealed class WitLinkTool : ICustomMcpTool
{
    internal const string ReadTimedOut =
        "The Azure DevOps request timed out while reading the work item's links; nothing was written.";

    private const string WorkItemUrlSegment = "/_apis/wit/workItems/";

    private static readonly (string LinkType, string Rel)[] LinkTypes =
    [
        ("parent", "System.LinkTypes.Hierarchy-Reverse"),
        ("child", "System.LinkTypes.Hierarchy-Forward"),
        ("related", "System.LinkTypes.Related"),
        ("predecessor", "System.LinkTypes.Dependency-Reverse"),
        ("successor", "System.LinkTypes.Dependency-Forward"),
        ("duplicate-of", "System.LinkTypes.Duplicate-Reverse"),
        ("duplicate", "System.LinkTypes.Duplicate-Forward"),
    ];

    private static readonly string ValidLinkTypes = string.Join(", ", LinkTypes.Select(t => t.LinkType));

    private readonly IAdoRestClient _ado;
    private readonly ILogger<WitLinkTool> _logger;

    public WitLinkTool(IAdoRestClient ado, ILogger<WitLinkTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_link";
    public object? Annotations => new { readOnlyHint = false };
    public string Description =>
        "Write operations: Adds or removes one link between two Azure DevOps work items. linkType is " +
        "relative to 'id': 'parent' makes targetId the parent of id, 'child' makes it a child, " +
        "'predecessor'/'successor' are dependency links, 'duplicate-of' marks id as a duplicate of " +
        "targetId, 'duplicate' marks targetId as a duplicate of id, 'related' is a plain related link. " +
        "This is how to set or change a parent — System.Parent cannot be written as a field. To move " +
        "an item to a new parent, remove the old 'parent' link, then add the new one. 'comment' (add " +
        "only) is stored on the link. Remove fails with an error, writing nothing, if no such link " +
        "exists, and is rejected by Azure DevOps if the item changed between the read and the write — " +
        "re-run it. Returns {status: LINKED|UNLINKED, id, targetId, linkType}.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name." },
            project = new { type = "string", description = "ADO project name of work item 'id'." },
            id = new { type = "integer", description = "Work item ID the link is added to / removed from." },
            targetId = new { type = "integer", description = "Work item ID at the other end of the link." },
            action = new { type = "string", @enum = new[] { "add", "remove" } },
            linkType = new
            {
                type = "string",
                @enum = LinkTypes.Select(t => t.LinkType).ToArray(),
                description = "Link type, from the point of view of 'id' (e.g. 'parent' = targetId is id's parent).",
            },
            comment = new { type = "string", description = "Optional link comment (add only)." },
        },
        required = new[] { "organization", "project", "id", "targetId", "action", "linkType" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var id = ToolArgs.RequireInt(arguments, "id");
        var targetId = ToolArgs.RequireInt(arguments, "targetId");
        if (targetId <= 0)
            throw new CallerArgumentException("'targetId' must be a positive work item ID.");
        var remove = ReadAction(arguments);
        var (linkType, rel) = ReadLinkType(arguments);
        if (targetId == id)
            throw new CallerArgumentException($"A work item cannot be linked to itself (id and targetId are both {id}).");

        _logger.LogInformation(
            "ado_bridge_wit_link: {Action} {LinkType} link {Id} -> {TargetId} in {Org}/{Project}",
            remove ? "remove" : "add", linkType, id, targetId, org, project);

        List<object> ops;
        if (remove)
        {
            JsonElement? workItem;
            try
            {
                workItem = await _ado.GetWorkItemAsync(org, project, id, ct).ConfigureAwait(false);
            }
            catch (AdoRestException ex)
            {
                return new McpToolResult($"Azure DevOps returned HTTP {ex.StatusCode}: {ex.Message}", IsError: true);
            }
            catch (HttpRequestException ex)
            {
                return new McpToolResult($"ADO request failed (transport): {ex.Message}", IsError: true);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new McpToolResult(ReadTimedOut, IsError: true);
            }

            if (workItem is null)
                return new McpToolResult($"Work item {id} not found.", IsError: true);

            var index = FindRelationIndex(workItem.Value, rel, targetId);
            if (index is null)
            {
                return new McpToolResult(
                    $"Work item {id} has no '{linkType}' link to work item {targetId}; nothing was removed.",
                    IsError: true);
            }

            ops = [];
            if (WitUpdateTool.ReadRev(workItem.Value) is { } rev)
                ops.Add(new { op = "test", path = "/rev", value = rev });
            ops.Add(new { op = "remove", path = $"/relations/{index}" });
        }
        else
        {
            var url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/_apis/wit/workItems/{targetId}";
            var comment = ToolArgs.GetString(arguments, "comment");
            object value = string.IsNullOrWhiteSpace(comment)
                ? new { rel, url }
                : new { rel, url, attributes = new { comment } };
            ops = [new { op = "add", path = "/relations/-", value }];
        }

        try
        {
            await _ado.UpdateWorkItemAsync(org, project, id, ops, ct).ConfigureAwait(false);
        }
        catch (AdoRestException ex)
        {
            return new McpToolResult($"Azure DevOps returned HTTP {ex.StatusCode}: {ex.Message}", IsError: true);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed (transport): {ex.Message}", IsError: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new McpToolResult(WitUpdateTool.TimedOut, IsError: true);
        }
        catch (JsonException)
        {
            return new McpToolResult(WitUpdateTool.UnreadableResponse, IsError: true);
        }

        return new McpToolResult(JsonSerializer.Serialize(new
        {
            status = remove ? "UNLINKED" : "LINKED",
            id,
            targetId,
            linkType,
        }));
    }

    private static bool ReadAction(JsonElement arguments)
        => ToolArgs.GetString(arguments, "action") switch
        {
            "add" => false,
            "remove" => true,
            _ => throw new CallerArgumentException("'action' is required and must be 'add' or 'remove'."),
        };

    private static (string LinkType, string Rel) ReadLinkType(JsonElement arguments)
    {
        var requested = ToolArgs.GetString(arguments, "linkType");
        foreach (var entry in LinkTypes)
        {
            if (entry.LinkType == requested)
                return entry;
        }

        throw new CallerArgumentException($"'linkType' is required and must be one of: {ValidLinkTypes}.");
    }

    /// <summary>
    /// Index in <paramref name="workItem"/>'s <c>relations</c> of the first entry whose
    /// <c>rel</c> is <paramref name="rel"/> and whose <c>url</c> points at work item
    /// <paramref name="targetId"/>. The id is parsed from the url's trailing segment because ADO
    /// returns relation urls carrying the project GUID, so the url never equals the one we add.
    /// </summary>
    private static int? FindRelationIndex(JsonElement workItem, string rel, int targetId)
    {
        if (!workItem.TryGetProperty("relations", out var relations) || relations.ValueKind != JsonValueKind.Array)
            return null;

        var index = 0;
        foreach (var relation in relations.EnumerateArray())
        {
            if (relation.ValueKind == JsonValueKind.Object &&
                relation.TryGetProperty("rel", out var relEl) && relEl.ValueKind == JsonValueKind.String &&
                string.Equals(relEl.GetString(), rel, StringComparison.OrdinalIgnoreCase) &&
                relation.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String &&
                ParseWorkItemId(urlEl.GetString()!) == targetId)
            {
                return index;
            }
            index++;
        }

        return null;
    }

    private static int? ParseWorkItemId(string url)
    {
        var at = url.LastIndexOf(WorkItemUrlSegment, StringComparison.OrdinalIgnoreCase);
        return at >= 0 && int.TryParse(
                   url.AsSpan(at + WorkItemUrlSegment.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
