using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class WitGetBatchSlimTool : ICustomMcpTool
{
    private const int MaxBatchSize = 200;

    private readonly IAdoRestClient _ado;
    private readonly IWorkItemFieldTypeCache _fieldTypes;
    private readonly ILogger<WitGetBatchSlimTool> _logger;

    public WitGetBatchSlimTool(
        IAdoRestClient ado, IWorkItemFieldTypeCache fieldTypes, ILogger<WitGetBatchSlimTool> logger)
    {
        _ado = ado;
        _fieldTypes = fieldTypes;
        _logger = logger;
    }

    public string Name => "ado_bridge_wit_get_batch";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Gets multiple Azure DevOps work items (up to 200) with all long-text fields " +
        "replaced by compact stub markers showing only the character count. Returns a JSON array of slim " +
        "work-item objects in the same order as the requested ids. " +
        "Use ado_bridge_download_field to fetch the full content of any stubbed field.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            ids = new
            {
                type = "array",
                items = new { type = "integer" },
                description = "Work-item numeric ids (maximum 200).",
                maxItems = MaxBatchSize,
            },
        },
        required = new[] { "organization", "project", "ids" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = arguments.GetProperty("organization").GetString()!;
        var project = arguments.GetProperty("project").GetString()!;
        var ids = arguments.GetProperty("ids")
            .EnumerateArray()
            .Select(e => e.GetInt32())
            .ToList();

        if (ids.Count == 0)
            return new McpToolResult("[]");

        if (ids.Count > MaxBatchSize)
            return new McpToolResult(
                $"ids array must not exceed {MaxBatchSize} items.", IsError: true);

        _logger.LogInformation(
            "ado_bridge_wit_get_batch: {Count} WIs in {Org}/{Project}", ids.Count, org, project);

        IReadOnlyList<JsonElement> workItems;
        IReadOnlySet<string> longTextFields;
        try
        {
            var witsTask = _ado.GetWorkItemsBatchAsync(org, project, ids, ct);
            var typesTask = _fieldTypes.GetLongTextFieldRefNamesAsync(org, ct);
            await Task.WhenAll(witsTask, typesTask).ConfigureAwait(false);
            workItems = await witsTask;
            longTextFields = await typesTask;
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed: {ex.Message}", IsError: true);
        }

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartArray();
            foreach (var wi in workItems)
                WitGetSlimTool.WriteSlimWorkItem(writer, wi, longTextFields);
            writer.WriteEndArray();
        }
        return new McpToolResult(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
