using AdoMcpBridge.Core.Abstractions;

namespace AdoMcpBridge.Core.OAuth;

public sealed class AuthorizeRequestValidator
{
    private readonly ITokenStore _store;
    public AuthorizeRequestValidator(ITokenStore store) => _store = store;

    public async ValueTask<(bool Ok, OAuthError? Error)> ValidateAsync(
        AuthorizeRequest req, CancellationToken ct)
    {
        if (req.ResponseType != "code")
            return (false, new OAuthError("unsupported_response_type", "only response_type=code is supported"));

        if (string.IsNullOrEmpty(req.State))
            return (false, OAuthError.InvalidRequest("state is required"));

        if (req.CodeChallengeMethod != "S256")
            return (false, OAuthError.InvalidRequest("code_challenge_method must be S256"));

        if (string.IsNullOrEmpty(req.CodeChallenge) || req.CodeChallenge.Length < 43)
            return (false, OAuthError.InvalidRequest("code_challenge missing or too short"));

        var client = await _store.FindClientAsync(req.ClientId, ct);
        if (client is null)
            return (false, OAuthError.InvalidClient("unknown client_id"));

        if (!client.RedirectUris.Contains(req.RedirectUri, StringComparer.Ordinal))
            return (false, OAuthError.InvalidRequest("redirect_uri not registered for this client"));

        return (true, null);
    }
}
