namespace AdoMcpBridge.Smoke;

internal static class SmokeEnvironment
{
    public const string BridgeUrlVar = "ADOMCP_BRIDGE_URL";
    public const string RefreshTokenVar = "ADOMCP_SMOKE_REFRESH_TOKEN";
    public const string ClientIdVar = "ADOMCP_SMOKE_CLIENT_ID";

    /// <summary>True when the bridge URL is configured (the minimum for any live smoke test).</summary>
    public static bool HasBridgeUrl =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BridgeUrlVar));

    /// <summary>True when URL + refresh token + client id are all configured.</summary>
    public static bool HasFullCredentials =>
        HasBridgeUrl
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RefreshTokenVar))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ClientIdVar));

    public static Uri RequireBridgeUrl()
    {
        var raw = Environment.GetEnvironmentVariable(BridgeUrlVar)
            ?? throw new InvalidOperationException(
                $"Smoke environment variable {BridgeUrlVar} is not set.");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{BridgeUrlVar} is not a valid absolute URI.");
        return uri;
    }

    public static string RequireRefreshToken() =>
        Environment.GetEnvironmentVariable(RefreshTokenVar)
            ?? throw new InvalidOperationException(
                $"Smoke environment variable {RefreshTokenVar} is not set.");

    public static string RequireClientId() =>
        Environment.GetEnvironmentVariable(ClientIdVar)
            ?? throw new InvalidOperationException(
                $"Smoke environment variable {ClientIdVar} is not set.");

    /// <summary>
    /// Returns a sentinel string safe to put in test output. Never returns
    /// any portion of <paramref name="value"/>. Length is exposed only as a
    /// shape hint for triage.
    /// </summary>
    public static string Redact(string? value) =>
        $"<redacted len={value?.Length ?? 0}>";
}
