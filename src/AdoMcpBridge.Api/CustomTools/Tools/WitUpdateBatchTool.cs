using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class WitUpdateBatchTool : ICustomMcpTool
{
    private const int MaxUpdates = 50;

    private static readonly JsonSerializerOptions OmitNulls =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly IAdoRestClient _ado;
    private readonly ILogger<WitUpdateBatchTool> _logger;

    public WitUpdateBatchTool(IAdoRestClient ado, ILogger<WitUpdateBatchTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_update_batch";
    public object? Annotations => new { readOnlyHint = false };
    public string Description =>
        "Write operations: Updates scalar fields on up to 50 Azure DevOps work items in one call. " +
        "Each entry in 'updates' is {id, fields} where 'fields' has the same shape and rules as " +
        "'ado_bridge_wit_update' (op 'set' default / 'clear'; long-text fields and System.Parent are " +
        "rejected). Every entry is validated before any write; then each work item is patched " +
        "separately, in order — this is NOT atomic. A failure on one item does not stop the others: " +
        "the result lists every item as {id, status: 'UPDATED' | 'FAILED', rev?, error?}.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name." },
            project = new { type = "string", description = "ADO project name." },
            updates = new
            {
                type = "array",
                minItems = 1,
                maxItems = MaxUpdates,
                description = "One entry per work item to update (1-50).",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "integer", description = "Work item ID." },
                        fields = WitUpdateTool.FieldsSchema,
                    },
                    required = new[] { "id", "fields" },
                },
            },
        },
        required = new[] { "organization", "project", "updates" },
    };

    private sealed record PlannedUpdate(int Id, List<object> Ops);

    private sealed record ItemResult(int Id, string Status, int? Rev = null, string? Error = null);

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var planned = Plan(arguments);

        _logger.LogInformation(
            "ado_bridge_wit_update_batch: updating {Count} work items in {Org}/{Project}",
            planned.Count, org, project);

        var results = new List<ItemResult>(planned.Count);
        foreach (var update in planned)
        {
            try
            {
                var updated = await _ado.UpdateWorkItemAsync(org, project, update.Id, update.Ops, ct)
                    .ConfigureAwait(false);
                results.Add(new ItemResult(update.Id, "UPDATED", Rev: WitUpdateTool.ReadRev(updated)));
            }
            catch (AdoRestException ex)
            {
                results.Add(new ItemResult(update.Id, "FAILED",
                    Error: $"Azure DevOps returned HTTP {ex.StatusCode}: {ex.Message}"));
            }
            catch (HttpRequestException ex)
            {
                results.Add(new ItemResult(update.Id, "FAILED",
                    Error: $"ADO request failed (transport): {ex.Message}"));
            }
        }

        var failed = results.Count(r => r.Status == "FAILED");
        _logger.LogInformation(
            "ado_bridge_wit_update_batch: {Updated} updated, {Failed} failed in {Org}/{Project}",
            results.Count - failed, failed, org, project);

        var payload = new
        {
            results = results.Select(r => new { id = r.Id, status = r.Status, rev = r.Rev, error = r.Error }),
        };
        return new McpToolResult(JsonSerializer.Serialize(payload, OmitNulls));
    }

    // Validates every entry before the first write so a caller mistake in entry N never leaves
    // entries 0..N-1 written and the rest not.
    private static List<PlannedUpdate> Plan(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("updates", out var updates) ||
            updates.ValueKind != JsonValueKind.Array ||
            updates.GetArrayLength() == 0)
        {
            throw new CallerArgumentException("'updates' is required and must be a non-empty array.");
        }

        if (updates.GetArrayLength() > MaxUpdates)
        {
            throw new CallerArgumentException(
                $"'updates' has {updates.GetArrayLength()} entries; the maximum is {MaxUpdates}. Split the batch.");
        }

        var planned = new List<PlannedUpdate>();
        var index = 0;
        foreach (var entry in updates.EnumerateArray())
        {
            try
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    throw new CallerArgumentException("entry must be an object {id, fields}.");
                var id = ToolArgs.RequireInt(entry, "id");
                var (ops, _) = WitUpdateTool.BuildPatch(entry);
                planned.Add(new PlannedUpdate(id, ops));
            }
            catch (CallerArgumentException ex)
            {
                throw new CallerArgumentException($"updates[{index}]: {ex.Message}");
            }
            index++;
        }

        return planned;
    }
}
