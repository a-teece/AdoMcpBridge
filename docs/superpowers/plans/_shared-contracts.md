# Shared Contracts (reference for all plans)

> Non-normative scaffolding so the per-subsystem plans agree on names,
> file paths, and interface shapes. If a plan conflicts with this file,
> this file wins — update plans, not the spec.

Source spec: [`docs/specs/2026-06-09-ado-mcp-bridge-design.md`](../specs/2026-06-09-ado-mcp-bridge-design.md).

## Solution & project layout

```
AdoMcpBridge.sln
src/
  AdoMcpBridge.Core/           # netstandard2.0 NOT used; target net10.0
    AdoMcpBridge.Core.csproj
  AdoMcpBridge.Api/            # ASP.NET Core host; targets net10.0
    AdoMcpBridge.Api.csproj
  AdoMcpBridge.Analyzers/      # Roslyn analyzers (netstandard2.0)
    AdoMcpBridge.Analyzers.csproj
tests/
  AdoMcpBridge.Core.Tests/
  AdoMcpBridge.Api.Tests/
  AdoMcpBridge.Analyzers.Tests/
  AdoMcpBridge.Smoke/
infra/                          # Bicep
pipelines/                      # GitHub Actions workflows in .github/workflows/
docs/
```

- Root namespace: `AdoMcpBridge.Core`, `AdoMcpBridge.Api`,
  `AdoMcpBridge.Analyzers`.
- `Directory.Build.props` at repo root sets `<TargetFramework>net10.0</TargetFramework>`
  (analyzer project overrides), `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>latest</LangVersion>`.

## Core interfaces (defined in `src/AdoMcpBridge.Core/Abstractions/`)

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IKeyVaultEncryptor
{
    ValueTask<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct);
    ValueTask<byte[]> DecryptAsync(byte[] ciphertext, CancellationToken ct);
}

public interface IEntraTokenClient
{
    ValueTask<EntraTokenResult> ExchangeAuthorizationCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct);

    ValueTask<EntraTokenResult> AcquireAdoTokenAsync(
        string entraRefreshToken, CancellationToken ct);
}

public sealed record EntraTokenResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string UserObjectId,
    string UserPrincipalName);

public interface ITokenStore
{
    ValueTask<RegisteredClient?> FindClientAsync(string clientId, CancellationToken ct);
    ValueTask AddClientAsync(RegisteredClient client, CancellationToken ct);

    ValueTask AddAuthorizationCodeAsync(AuthorizationCodeRecord code, CancellationToken ct);
    ValueTask<AuthorizationCodeRecord?> ConsumeAuthorizationCodeAsync(string code, CancellationToken ct);

    ValueTask AddTokenAsync(TokenRecord token, CancellationToken ct);
    ValueTask<TokenRecord?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct);
    ValueTask<TokenRecord?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct);
    ValueTask RevokeTokenAsync(string refreshTokenHash, CancellationToken ct);
    ValueTask ReplaceTokenAsync(TokenRecord oldToken, TokenRecord newToken, CancellationToken ct);
}

public sealed record RegisteredClient(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    DateTimeOffset CreatedAt);

public sealed record AuthorizationCodeRecord(
    string Code,
    string ClientId,
    string RedirectUri,
    string PkceChallenge,
    string PkceMethod,         // always "S256"
    string EntraRefreshTokenEncrypted, // base64
    string UserObjectId,
    string UserPrincipalName,
    DateTimeOffset ExpiresAt);

public sealed record TokenRecord(
    string AccessTokenHash,
    string RefreshTokenHash,
    string ClientId,
    string EntraRefreshTokenEncrypted, // base64
    string UserObjectId,
    string UserPrincipalName,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    DateTimeOffset CreatedAt);
```

## Token lifetimes (from spec §5)

- Wrapper access token: **1 hour**.
- Wrapper refresh token: **14 days** (rolling — refresh issues new pair).
- Authorization code: **60 seconds**.

## Token format

- Opaque, base64url, 32 bytes from `RandomNumberGenerator.GetBytes(32)`.
- Store **SHA-256 hashes** (hex lowercase) in DB — never plaintext.
- Prefix-less; the wrapper recognises them by length + DB lookup.

## OAuth metadata document

Served at `/.well-known/oauth-authorization-server`:

```json
{
  "issuer": "https://<host>",
  "authorization_endpoint": "https://<host>/authorize",
  "token_endpoint": "https://<host>/token",
  "registration_endpoint": "https://<host>/register",
  "revocation_endpoint": "https://<host>/revoke",
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"],
  "token_endpoint_auth_methods_supported": ["none"],
  "scopes_supported": ["mcp"]
}
```

## Database (EF Core, real SQL)

DbContext: `AdoMcpBridge.Core.Data.BridgeDbContext` with `DbSet<>`s for
`Clients`, `AuthorizationCodes`, `Tokens`. Migrations in
`src/AdoMcpBridge.Core/Data/Migrations/`. Initial migration named
`InitialSchema`.

- `Clients.ClientId` PK, string(64).
- `AuthorizationCodes.Code` PK, string(64), indexed on `ExpiresAt`.
- `Tokens.AccessTokenHash` PK, string(64); unique index on
  `RefreshTokenHash`; indexed on `RefreshTokenExpiresAt`.

## Roslyn analyzer diagnostic IDs

- `ADOMCP001` — token/code/verifier symbol flowed into `ILogger` argument.
- `ADOMCP002` — `[ExcludeFromCodeCoverage]` without non-empty `Justification`.

## YARP proxy

- Cluster id: `ado-mcp`.
- Destination: `https://mcp.dev.azure.com` (overridable via config
  `AdoMcp:UpstreamBaseUrl`).
- Route id: `mcp`, path `/mcp/{**catch-all}`.
- Middleware order:
  1. Correlation-id (W3C TraceContext).
  2. Bearer-validation (looks up `TokenRecord` by SHA-256 of bearer).
  3. Entra-token-swap (calls `IEntraTokenClient.AcquireAdoTokenAsync`).
  4. Header passthrough (`X-MCP-Toolsets`, `X-MCP-Readonly`,
     `X-MCP-Tools`, `X-MCP-Insiders`).
  5. YARP forwarder.

## OpenTelemetry metric names

(All under meter `AdoMcpBridge`.)

- `oauth.dcr.registrations` (counter)
- `oauth.token.issued` (counter, tags: `grant_type`)
- `oauth.token.refreshed` (counter)
- `oauth.token.rejected` (counter, tags: `reason`)
- `entra.refresh.duration_ms` (histogram)
- `proxy.upstream.duration_ms` (histogram, tags: `status_class`)
- `proxy.upstream.errors` (counter, tags: `status_code`)
- `proxy.in_flight` (up-down counter)

## Configuration keys (`appsettings.json`)

```jsonc
{
  "AdoMcp": {
    "Issuer": "https://localhost:5001",
    "UpstreamBaseUrl": "https://mcp.dev.azure.com",
    "Entra": {
      "TenantId": "...",
      "ClientId": "...",
      "CertificateName": "ado-mcp-bridge",
      "Authority": "https://login.microsoftonline.com/{TenantId}/v2.0",
      "Scopes": ["499b84ac-1321-427f-aa17-267ca6975798/user_impersonation", "offline_access"]
    },
    "KeyVault": {
      "VaultUri": "https://kv-adomcp-dev.vault.azure.net/",
      "DekName": "token-dek"
    },
    "Database": {
      "ConnectionString": "Server=...;Authentication=Active Directory Default;"
    }
  }
}
```

## Naming for deployed Azure resources

Per env (`dev` / `prod`):

- Resource group: `rg-adomcp-{env}`
- Container App: `ca-adomcp-{env}`
- Container App Environment: `cae-adomcp-{env}`
- ACR: `cradomcp{env}` (no hyphens — ACR rule)
- Key Vault: `kv-adomcp-{env}` (≤24 chars, lowercased)
- SQL server: `sql-adomcp-{env}`
- SQL DB: `sqldb-adomcp`
- Log Analytics: `log-adomcp-{env}`
- App Insights: `appi-adomcp-{env}`
- User-assigned managed identity: `id-adomcp-{env}`

## Plan list (dependency order)

1. `2026-06-09-foundation.md` — solution skeleton, `Directory.Build.props`, analyzers project + 2 rules, shared abstractions (`IClock`, `IKeyVaultEncryptor`, `IEntraTokenClient`, `ITokenStore` interfaces and records), in-memory test token store.
2. `2026-06-09-token-store.md` — EF Core `BridgeDbContext`, real-SQL integration tests, Key Vault DEK encryptor (production impl of `IKeyVaultEncryptor`).
3. `2026-06-09-oauth-as.md` — `/.well-known`, `/register`, `/authorize`, `/token`, `/revoke`, consent page, PKCE, opaque token minter, depends on (1) + (2).
4. `2026-06-09-entra-client.md` — MSAL.NET confidential-client + cert from Key Vault; production impl of `IEntraTokenClient`; WireMock-based integration tests.
5. `2026-06-09-mcp-proxy.md` — YARP cluster, bearer-validation middleware, Entra-swap middleware, header passthrough, `/connector-info.json`.
6. `2026-06-09-infra-bicep.md` — Bicep modules for all resources in §6, parameter files for dev/prod, `allowedIpRanges` parameter.
7. `2026-06-09-cicd-release.md` — GitHub Actions: build/test/coverage, container build → GHCR, cosign signing, Bicep packaging, `deploy.ps1`, reusable CD workflow, SemVer release notes.
8. `2026-06-09-observability-runbook.md` — OpenTelemetry wiring, metrics, App Insights exporter, alert rules in Bicep, runbook + 6 scenarios + Kusto queries, PR-template alert gate.
9. `2026-06-09-smoke-connector.md` — Smoke test project (`AdoMcpBridge.Smoke`), nightly + on-tag GHA, auto-issue-on-failure, Claude Desktop connector card endpoint `/connector-info.json` (if not delivered in 5).
