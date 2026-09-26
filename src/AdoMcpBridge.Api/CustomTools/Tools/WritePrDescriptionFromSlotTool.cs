using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdoMcpBridge.Core.BlobStorage;

namespace AdoMcpBridge.Api.CustomTools.Tools;

internal sealed class WritePrDescriptionFromSlotTool : ICustomMcpTool
{
    // ADO caps a pull-request description at 4,000 characters (UTF-16 code units, which is
    // exactly what C#'s string.Length reports). Checked up front so an over-limit payload
    // fails before any ADO call is made and the slot is left intact for a shorter retry.
    private const int MaxDescriptionUnits = 4000;

    private readonly IBlobSlotStore _blobs;
    private readonly IAdoRestClient _ado;
    private readonly ILogger<WritePrDescriptionFromSlotTool> _logger;

    public WritePrDescriptionFromSlotTool(
        IBlobSlotStore blobs, IAdoRestClient ado, ILogger<WritePrDescriptionFromSlotTool> logger)
    {
        _blobs = blobs;
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_write_pr_description_from_slot";
    public object? Annotations => new { readOnlyHint = false };
    public string Description =>
        "Write operations: Transfers content from a previously created upload slot into an Azure DevOps " +
        "pull request's description. The bridge verifies the SHA-256 hash and enforces the 4,000 UTF-16-unit " +
        "ADO PR-description limit up front — an over-limit description is rejected before any ADO call, so " +
        "shorten it and retry. Returns {\"status\":\"WRITTEN\",\"charCount\":N} on success. " +
        "Call ado_bridge_create_upload_slot first to obtain a slot, then upload the content to it. " +
        "The sha256 must be the lowercase hex SHA-256 of the raw UTF-8 bytes of the uploaded file.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name." },
            project = new { type = "string", description = "ADO project name." },
            repositoryId = new { type = "string", description = "Repository name or GUID that owns the pull request." },
            pullRequestId = new { type = "integer", description = "Pull-request numeric id." },
            slotId = new { type = "string", description = "Slot ID returned by ado_bridge_create_upload_slot." },
            sha256 = new { type = "string", description = "Lowercase hex SHA-256 of the raw UTF-8 bytes of the uploaded content." },
        },
        required = new[] { "organization", "project", "repositoryId", "pullRequestId", "slotId", "sha256" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var repositoryId = ToolArgs.RequireString(arguments, "repositoryId");
        var pullRequestId = ToolArgs.RequireInt(arguments, "pullRequestId");
        var slotId = ToolArgs.RequireString(arguments, "slotId");
        var expectedSha = ToolArgs.RequireString(arguments, "sha256").ToLowerInvariant();

        // 1. Download the uploaded content.
        byte[] rawBytes;
        try
        {
            rawBytes = await _blobs.ReadSlotAsync(slotId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read slot {SlotId}", slotId);
            return new McpToolResult($"Failed to read upload slot '{slotId}': {ex.Message}", IsError: true);
        }

        // 2. Verify SHA-256 (over the raw bytes).
        var actualSha = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
        if (actualSha != expectedSha)
        {
            _logger.LogWarning(
                "Slot {SlotId} SHA-256 mismatch: expected {Expected} actual {Actual}",
                slotId, expectedSha, actualSha);
            return new McpToolResult(
                $"SHA-256 mismatch. expected={expectedSha} actual={actualSha}", IsError: true);
        }

        var content = Encoding.UTF8.GetString(rawBytes);

        // 3. Enforce the ADO length limit UP FRONT — before any ADO call. Leave the slot in
        // place so the caller can retry with shorter content without re-uploading.
        if (content.Length > MaxDescriptionUnits)
        {
            _logger.LogWarning(
                "ado_bridge_write_pr_description_from_slot: PR {Id} rejected — {Chars} UTF-16 units over the {Max} limit",
                pullRequestId, content.Length, MaxDescriptionUnits);
            return new McpToolResult(
                $"PR description is {content.Length} UTF-16 units, over the {MaxDescriptionUnits} limit. " +
                "Shorten it before writing.", IsError: true);
        }

        // 4. Write the description to the pull request.
        try
        {
            await _ado.UpdatePullRequestDescriptionAsync(
                    org, project, repositoryId, pullRequestId, content, ct)
                  .ConfigureAwait(false);
        }
        catch (AdoRestException ex)
        {
            return new McpToolResult(
                $"Azure DevOps returned HTTP {ex.StatusCode}: {ex.Message}", IsError: true);
        }
        catch (HttpRequestException ex)
        {
            return new McpToolResult($"ADO request failed (transport): {ex.Message}", IsError: true);
        }

        // 5. Clean up the slot (best-effort — the lifecycle policy sweeps orphans).
        try
        {
            await _blobs.DeleteSlotAsync(slotId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete slot {SlotId} after write", slotId);
        }

        _logger.LogInformation(
            "ado_bridge_write_pr_description_from_slot: PR {Id} status=WRITTEN chars={Chars}",
            pullRequestId, content.Length);

        return new McpToolResult(
            JsonSerializer.Serialize(new { status = "WRITTEN", charCount = content.Length }));
    }
}
