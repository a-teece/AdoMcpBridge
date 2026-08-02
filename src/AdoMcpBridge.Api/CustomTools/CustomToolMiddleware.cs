using System.Text;
using System.Text.Json;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Core.Abstractions;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Intercepts MCP JSON-RPC requests on the /mcp branch and handles custom
/// tool calls locally, short-circuiting the YARP reverse proxy.
/// Also injects custom tool definitions into tools/list responses from upstream.
/// Must be placed after <c>EntraTokenSwapMiddleware</c> (auth is required) and
/// before the YARP endpoint.
/// Before invoking any custom tool it performs its OWN second OBO/refresh-token
/// swap against the stored Entra refresh token, requesting the classic-ADO-REST
/// scopes (<c>EntraOptions.AdoRestScopes</c>) rather than the MCP-server scopes
/// that <c>EntraTokenSwapMiddleware</c> already put on the Authorization header —
/// the native tools call the classic Azure DevOps REST API, whose resource
/// rejects the MCP-audience token. The resulting ADO-REST-audience token is
/// stashed on <c>HttpContext.Items[AdoRestAccessToken]</c> for the tool's
/// REST client to read.
/// </summary>
internal sealed class CustomToolMiddleware
{
    // Streamable-HTTP servers assign the session id on this RESPONSE header at
    // initialize time; clients echo it back on this REQUEST header thereafter.
    private const string McpSessionIdHeader = "Mcp-Session-Id";

    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<ICustomMcpTool> _tools;
    private readonly IMcpSessionRegistry _sessions;
    private readonly ILogger<CustomToolMiddleware> _logger;

    public CustomToolMiddleware(
        RequestDelegate next,
        IEnumerable<ICustomMcpTool> tools,
        IMcpSessionRegistry sessions,
        ILogger<CustomToolMiddleware> logger)
    {
        _next = next;
        _tools = tools.ToList();
        _sessions = sessions;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IKeyVaultEncryptor encryptor, IEntraTokenClient entra)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Enable buffering so the body can be read and then rewound for YARP.
        context.Request.EnableBuffering();

        JsonDocument? doc = null;
        try
        {
            doc = await TryParseBodyAsync(context.Request, context.RequestAborted);
        }
        catch
        {
            // Ignore parse failures — not a JSON-RPC request, pass through.
        }

        if (doc is null)
        {
            context.Request.Body.Seek(0, SeekOrigin.Begin);
            await _next(context);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            var id = root.TryGetProperty("id", out var idEl) ? idEl : (JsonElement?)null;

            // An initialize response is where upstream assigns the session id and declares
            // its capabilities — intercept it to record the session as born here and to
            // advertise listChanged, independent of the stale-session notification path.
            if (method == "initialize")
            {
                await HandleInitializeAsync(context);
                return;
            }

            // A stale session (born before this deploy) needs exactly one refresh hint.
            // Only requests that expect a response body (those carrying an id) can carry a
            // notification frame; notification-only posts get 202/empty and are left alone.
            var sessionId = context.Request.Headers[McpSessionIdHeader].ToString();
            var notify = id.HasValue && _sessions.ShouldNotify(sessionId);

            if (method == "tools/call" &&
                root.TryGetProperty("params", out var p) &&
                p.TryGetProperty("name", out var nameEl))
            {
                var toolName = nameEl.GetString();
                var tool = _tools.FirstOrDefault(t => t.Name == toolName);
                if (tool is not null)
                {
                    // Custom-tool responses are locally generated plain JSON, never SSE, so
                    // there is nothing to inject a notification frame into — skip it here.
                    var args = p.TryGetProperty("arguments", out var a) ? a : default;
                    await HandleToolCallAsync(context, tool, args, id, encryptor, entra);
                    return;
                }

                if (toolName == WitWorkItemWriteArgumentNormalizer.ToolName &&
                    await TryForwardNormalizedWitWorkItemWriteAsync(context, root, id, notify, sessionId))
                {
                    return;
                }
            }

            if (method == "tools/list")
            {
                await HandleToolsListAsync(context, notify, sessionId);
                return;
            }

            context.Request.Body.Seek(0, SeekOrigin.Begin);
            await ForwardToNextAsync(context, notify, sessionId);
            return;
        }
    }

    /// <summary>
    /// Buffers the upstream initialize response, patches
    /// <c>result.capabilities.tools.listChanged = true</c> (so clients honour future
    /// <c>tools/list_changed</c> notifications), and records the assigned session id — read
    /// from the <c>Mcp-Session-Id</c> RESPONSE header — as born in this process so it is
    /// never itself treated as stale. Does nothing with the session if the header is absent.
    /// </summary>
    private async Task HandleInitializeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            context.Request.Body.Seek(0, SeekOrigin.Begin);
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        var patched = JsonRpcHelpers.PatchInitializeListChanged(buffer.ToArray());

        var sessionId = context.Response.Headers[McpSessionIdHeader].ToString();
        if (!string.IsNullOrEmpty(sessionId))
        {
            _sessions.MarkBorn(sessionId);
            _logger.LogDebug("Recorded MCP session born in this process: {SessionIdPrefix}", Prefix(sessionId));
        }

        context.Response.ContentLength = patched.Length;
        await originalBody.WriteAsync(patched, context.RequestAborted);
    }

    /// <summary>
    /// Forwards to the next middleware. When <paramref name="notify"/> is false the response
    /// streams straight through untouched (no buffering overhead on the common path).
    /// When true the response is buffered so a <c>tools/list_changed</c> frame can be
    /// prepended if — and only if — it is an SSE body; the session is then marked notified.
    /// </summary>
    private async Task ForwardToNextAsync(HttpContext context, bool notify, string sessionId)
    {
        if (!notify)
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        await WriteWithOptionalNotificationAsync(context, originalBody, buffer.ToArray(), sessionId, notify: true);
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="target"/>, first prepending a
    /// <c>tools/list_changed</c> frame when the response is a successful (2xx) SSE body.
    /// Marks the session notified only after a frame is actually injected — a plain-JSON or
    /// error response is written verbatim and left un-notified so the next SSE response can
    /// carry the hint instead.
    /// </summary>
    private async Task WriteWithOptionalNotificationAsync(
        HttpContext context, Stream target, byte[] bytes, string sessionId, bool notify)
    {
        var toWrite = bytes;
        if (notify &&
            context.Response.StatusCode is >= 200 and < 300 &&
            JsonRpcHelpers.TryPrependToolsListChangedFrame(bytes, out var injected))
        {
            toWrite = injected;
            _sessions.MarkNotified(sessionId);
            _logger.LogDebug("Injected tools/list_changed for stale MCP session: {SessionIdPrefix}", Prefix(sessionId));
        }

        context.Response.ContentLength = toWrite.Length;
        await target.WriteAsync(toWrite, context.RequestAborted);
    }

    // A short, truncated prefix for correlating log lines without emitting a full session
    // id (a bearer-like value). Math.Min keeps it branch-free; real session ids are long
    // GUIDs, so this only ever reveals a leading fragment.
    private static string Prefix(string sessionId) =>
        string.Concat(sessionId.AsSpan(0, Math.Min(8, sessionId.Length)), "…");

    private async Task HandleToolCallAsync(
        HttpContext context, ICustomMcpTool tool, JsonElement arguments, JsonElement? id,
        IKeyVaultEncryptor encryptor, IEntraTokenClient entra)
    {
        _logger.LogInformation("Handling custom tool call: {Tool}", tool.Name);

        // The native tools call the classic Azure DevOps REST API directly, which
        // needs a token audienced for that resource — NOT the MCP-server token
        // EntraTokenSwapMiddleware placed on the Authorization header. Redeem the
        // stored refresh token a second time for the ADO-REST scopes and stash the
        // result for HttpContextAdoAccessTokenProvider to read. TokenRecord is
        // guaranteed present: BearerAuthenticationMiddleware set it earlier in the
        // pipeline and this middleware only runs after auth succeeds.
        try
        {
            var record = (TokenRecord)context.Items[HttpContextItemKeys.TokenRecord]!;
            var cipher = Convert.FromBase64String(record.EntraRefreshTokenEncrypted);
            var plaintext = await encryptor.DecryptAsync(cipher, context.RequestAborted);
            var refreshToken = Encoding.UTF8.GetString(plaintext);
            var swap = await entra.AcquireAdoRestTokenAsync(refreshToken, context.RequestAborted);
            context.Items[HttpContextItemKeys.AdoRestAccessToken] = swap.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ADO REST token acquisition failed for tool {Tool}", tool.Name);
            await JsonRpcHelpers.WriteErrorAsync(
                context.Response, id, -32000, "ADO authentication failed", context.RequestAborted);
            return;
        }

        McpToolResult result;
        try
        {
            result = await tool.InvokeAsync(arguments, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in custom tool {Tool}", tool.Name);
            await JsonRpcHelpers.WriteErrorAsync(
                context.Response, id, -32603, "Internal error", context.RequestAborted);
            return;
        }

        await JsonRpcHelpers.WriteResultAsync(context.Response, id, result, context.RequestAborted);
    }

    /// <summary>
    /// Coerces wit_work_item_write's array-valued parameters into real JSON arrays
    /// before forwarding to upstream — see <see cref="WitWorkItemWriteArgumentNormalizer"/>.
    /// Returns <see langword="true"/> if the request was handled (forwarded with a
    /// rewritten body, or rejected with a JSON-RPC error); <see langword="false"/> if no
    /// normalisation was needed and the caller should fall through to the normal
    /// unmodified pass-through.
    /// </summary>
    private async Task<bool> TryForwardNormalizedWitWorkItemWriteAsync(
        HttpContext context, JsonElement root, JsonElement? id, bool notify, string sessionId)
    {
        byte[]? rewritten;
        try
        {
            rewritten = WitWorkItemWriteArgumentNormalizer.NormalizeRequestBody(root);
        }
        catch (WitWorkItemWriteArgumentException ex)
        {
            _logger.LogWarning(ex, "Rejecting malformed wit_work_item_write arguments");
            await JsonRpcHelpers.WriteErrorAsync(context.Response, id, -32602, ex.Message, context.RequestAborted);
            return true;
        }

        if (rewritten is null) return false;

        _logger.LogDebug("Normalised wit_work_item_write array arguments before forwarding upstream");
        context.Request.Body = new MemoryStream(rewritten);
        context.Request.ContentLength = rewritten.Length;
        await ForwardToNextAsync(context, notify, sessionId);
        return true;
    }

    private async Task HandleToolsListAsync(HttpContext context, bool notify, string sessionId)
    {
        // Buffer the upstream response so we can inject our tool definitions.
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            context.Request.Body.Seek(0, SeekOrigin.Begin);
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        var responseBytes = buffer.ToArray();

        // Compose with the stale-session refresh: inject the native tools first, then let
        // the notification frame be prepended to the (possibly rewritten) SSE body so a
        // stale session gets both the extra tools AND the list_changed hint in one response.
        var modified = JsonRpcHelpers.InjectToolsIntoListResponse(responseBytes, _tools);

        await WriteWithOptionalNotificationAsync(context, originalBody, modified, sessionId, notify);
    }

    private static async Task<JsonDocument?> TryParseBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength == 0) return null;
        var contentType = request.ContentType ?? string.Empty;
        if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            return await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
