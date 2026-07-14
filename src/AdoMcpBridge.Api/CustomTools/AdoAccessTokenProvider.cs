using AdoMcpBridge.Api.Proxy;

namespace AdoMcpBridge.Api.CustomTools;

/// <summary>
/// Supplies the ADO-scoped access token that the native tools use to call the
/// Azure DevOps REST API on behalf of the current caller.
/// </summary>
internal interface IAdoAccessTokenProvider
{
    /// <summary>
    /// Returns the caller's delegated ADO access token for the current request.
    /// Throws <see cref="InvalidOperationException"/> if no request-scoped token
    /// is available — the OAuth wrapper pipeline guarantees one is present by the
    /// time a native tool runs, so its absence is a wiring bug, not a runtime case.
    /// </summary>
    string GetAccessToken();
}

/// <summary>
/// Reads the caller's ADO-REST-scoped delegated token that
/// <c>CustomToolMiddleware</c> stashed on
/// <c>HttpContext.Items[AdoRestAccessToken]</c> before invoking the tool. That
/// middleware performs a dedicated OBO/refresh-token swap for the classic Azure
/// DevOps REST resource — separate from the MCP-server token on the request's
/// Authorization header, which the classic REST API rejects — so the token this
/// provider returns is audienced correctly for <c>https://dev.azure.com</c> and
/// every ADO call is attributed to (and permission-scoped to) the real end user,
/// not the bridge's managed identity.
/// </summary>
/// <remarks>
/// Registered as a singleton. This is safe despite reading per-request state
/// because <see cref="IHttpContextAccessor"/> resolves the ambient
/// <see cref="HttpContext"/> via <c>AsyncLocal</c> at call time, not at
/// construction time.
/// </remarks>
internal sealed class HttpContextAdoAccessTokenProvider : IAdoAccessTokenProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextAdoAccessTokenProvider(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string GetAccessToken()
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No active HttpContext; the ADO access token can only be sourced " +
                "from within the request pipeline.");

        // CustomToolMiddleware always populates this item (via its ADO-REST OBO
        // swap) before invoking any native tool, so a missing/empty value here is
        // an unreachable wiring bug, not a runtime condition to swallow silently.
        if (context.Items[HttpContextItemKeys.AdoRestAccessToken] is not string token ||
            token.Length == 0)
        {
            throw new InvalidOperationException(
                "No ADO REST access token on HttpContext; CustomToolMiddleware must " +
                "run and complete its ADO token swap before any native tool call.");
        }

        return token;
    }
}
