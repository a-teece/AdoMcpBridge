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
/// Reads the caller's ADO access token from the current request's
/// <c>Authorization</c> header. By the time a native tool invokes the REST
/// client, <c>EntraTokenSwapMiddleware</c> has already swapped the incoming
/// wrapper token for an ADO-scoped delegated token and written it back onto the
/// request header, so this provider simply passes that token straight through —
/// every ADO call is attributed to the real end user and honours their own ADO
/// permissions. (The bridge managed identity therefore no longer needs to be a
/// member of the ADO organisation for this code path.)
/// </summary>
/// <remarks>
/// Registered as a singleton. This is safe despite reading per-request state
/// because <see cref="IHttpContextAccessor"/> resolves the ambient
/// <see cref="HttpContext"/> via <c>AsyncLocal</c> at call time, not at
/// construction time.
/// </remarks>
internal sealed class HttpContextAdoAccessTokenProvider : IAdoAccessTokenProvider
{
    private const string BearerPrefix = "Bearer ";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextAdoAccessTokenProvider(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string GetAccessToken()
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No active HttpContext; the ADO access token can only be sourced " +
                "from within the request pipeline.");

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) ||
            !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Request has no Bearer Authorization header; EntraTokenSwapMiddleware " +
                "must run before any native tool call.");
        }

        var token = header[BearerPrefix.Length..].Trim();
        if (token.Length == 0)
        {
            throw new InvalidOperationException(
                "Authorization header carried an empty Bearer token.");
        }

        return token;
    }
}
