using System.Text;
using System.Text.Json;

namespace AdoMcpBridge.Api.CustomTools.Tools;

/// <summary>
/// Returns a repository file's TEXT content INLINE so the model can read it directly in the
/// response. The upstream <c>repo_file</c> <c>get_content</c> action returns a file as a binary
/// MCP resource the client saves to disk, so the model never sees the text; this tool decodes
/// UTF-8 text (up to a 1&#160;MiB cap) and returns it as-is. For binary or larger files, and for
/// directory listings, the upstream <c>repo_file</c> tool remains the right path.
/// </summary>
internal sealed class ReadFileTool : ICustomMcpTool
{
    // A NUL byte or a strict-UTF-8 decode failure marks content as binary; text above this
    // size is refused so a large file cannot flood the model context.
    private const int MaxInlineBytes = 1_048_576;

    private readonly IAdoRestClient _ado;
    private readonly ILogger<ReadFileTool> _logger;

    public ReadFileTool(IAdoRestClient ado, ILogger<ReadFileTool> logger)
    {
        _ado = ado;
        _logger = logger;
    }

    public string Name => "ado_bridge_read_file";
    public object? Annotations => new { readOnlyHint = true };

    public string Description =>
        "Read operations: Returns a repository file's TEXT content INLINE so it can be read " +
        "directly in the response. Use this for source/text files up to 1 MiB. Unlike the " +
        "upstream repo_file get_content action — which returns the file as a binary resource the " +
        "client saves to disk rather than showing inline — this decodes UTF-8 text and returns it " +
        "as-is. For binary files, files larger than 1 MiB, or directory listings, use the upstream " +
        "repo_file tool (get_content / list_directory) instead. " +
        "Optionally pin a 'version' (branch/tag/commit) and 'versionType' (Branch/Commit/Tag); " +
        "defaults to the repository's default branch.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            organization = new { type = "string", description = "ADO organisation name (e.g. my-org)." },
            project = new { type = "string", description = "ADO project name." },
            repositoryId = new { type = "string", description = "Repository name or GUID." },
            path = new { type = "string", description = "Full file path, e.g. /src/Foo.cs" },
            version = new
            {
                type = "string",
                description = "Optional branch/tag/commit; defaults to the repo default branch.",
            },
            versionType = new
            {
                type = "string",
                @enum = new[] { "Branch", "Commit", "Tag" },
                description = "Optional version kind; interprets 'version'. Defaults to Branch.",
            },
        },
        required = new[] { "organization", "project", "repositoryId", "path" },
    };

    public async Task<McpToolResult> InvokeAsync(JsonElement arguments, CancellationToken ct)
    {
        var org = ToolArgs.RequireString(arguments, "organization");
        var project = ToolArgs.RequireString(arguments, "project");
        var repositoryId = ToolArgs.RequireString(arguments, "repositoryId");
        var path = ToolArgs.RequireString(arguments, "path");
        var version = ToolArgs.GetString(arguments, "version");
        var versionType = ToolArgs.GetString(arguments, "versionType");

        _logger.LogInformation(
            "ado_bridge_read_file: {Path} in {Org}/{Project} repo {Repo}", path, org, project, repositoryId);

        AdoRepoItemContent? item;
        try
        {
            item = await _ado.GetRepoItemContentAsync(org, project, repositoryId, path, version, versionType, ct)
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

        if (item is null)
            return new McpToolResult($"File not found: {path}", IsError: true);

        var content = item.Content;
        if (content.Length > MaxInlineBytes)
            return new McpToolResult(
                $"File is {content.Length} bytes, over the {MaxInlineBytes}-byte inline read limit. " +
                "Use the upstream repo_file get_content action for large files.", IsError: true);

        if (LooksBinary(content, out var text))
            return new McpToolResult(
                $"'{path}' appears to be binary (content-type {item.ContentType}); " +
                "use the upstream repo_file get_content action to download it.", IsError: true);

        _logger.LogInformation(
            "ado_bridge_read_file: {Path} returned inline chars={CharCount}", path, text!.Length);

        return new McpToolResult(text);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="content"/> should be treated as binary
    /// (it contains a NUL byte, or fails strict UTF-8 decoding). On a clean decode returns
    /// <see langword="false"/> and sets <paramref name="text"/> to the decoded string.
    /// </summary>
    private static bool LooksBinary(byte[] content, out string? text)
    {
        text = null;
        if (Array.IndexOf(content, (byte)0) >= 0)
            return true;

        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content);
            return false;
        }
        catch (DecoderFallbackException)
        {
            return true;
        }
    }
}
