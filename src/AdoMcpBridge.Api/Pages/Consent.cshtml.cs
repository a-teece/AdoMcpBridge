using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AdoMcpBridge.Api.Pages;

public sealed class ConsentModel : PageModel
{
    private readonly AuthorizeRequestValidator _validator;
    private readonly IAuthorizationSessionCache _cache;
    private readonly WrapperTokenMinter _minter;
    private readonly ITokenStore _store;
    private readonly IClock _clock;

    public ConsentModel(
        AuthorizeRequestValidator v,
        IAuthorizationSessionCache c,
        WrapperTokenMinter m,
        ITokenStore s,
        IClock clock)
    {
        _validator = v; _cache = c; _minter = m; _store = s; _clock = clock;
    }

    public string SessionId { get; private set; } = "";
    public string ClientName { get; private set; } = "";
    public string RedirectUri { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = "response_type")] string responseType = "",
        [FromQuery(Name = "client_id")] string clientId = "",
        [FromQuery(Name = "redirect_uri")] string redirectUri = "",
        [FromQuery(Name = "code_challenge")] string codeChallenge = "",
        [FromQuery(Name = "code_challenge_method")] string codeChallengeMethod = "",
        [FromQuery(Name = "state")] string state = "",
        [FromQuery(Name = "scope")] string? scope = null,
        CancellationToken ct = default)
    {
        var req = new AuthorizeRequest(responseType, clientId, redirectUri,
            codeChallenge, codeChallengeMethod, state, scope);
        var (ok, err) = await _validator.ValidateAsync(req, ct);
        if (!ok) return new ContentResult { StatusCode = 400, ContentType = "application/json", Content = err!.ToJson() };

        var sessionId = _minter.MintOpaque();
        var entraVerifier = _minter.MintOpaque();
        var entraState = _minter.MintOpaque();
        await _cache.PutAsync(new AuthorizationSession(
            sessionId, clientId, redirectUri, codeChallenge, codeChallengeMethod, state,
            entraVerifier, entraState, _clock.UtcNow.AddMinutes(10)), ct);

        var client = await _store.FindClientAsync(clientId, ct);
        SessionId = sessionId;
        ClientName = client!.ClientName;
        RedirectUri = redirectUri;
        return Page();
    }
}
