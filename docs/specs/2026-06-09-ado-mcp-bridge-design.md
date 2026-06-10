# ADO MCP Bridge — Design Spec

**Date:** 2026-06-09
**Author:** Andrew Teece
**Status:** Draft for review

## 1. Purpose & Scope

A C# service that wraps Microsoft's **Remote Azure DevOps MCP Server**
(`https://mcp.dev.azure.com/{org}`) so that **Claude Code**, **Claude
Desktop**, and other MCP clients that do not yet support Entra dynamic
client registration can use it today. Microsoft's own FAQ identifies
OAuth dynamic client registration (DCR) against Entra as the blocker;
this wrapper resolves that by:

- Acting as the OAuth **Authorization Server** facing Claude (DCR is
  trivial because we own the surface).
- Acting as a **static, admin-consented OAuth client** facing Entra.

The bridge is intended as **open source** on GitHub (community fix for
an MS-acknowledged gap). The author is an org admin for his Claude
organization and will also register the deployed instance as a Claude
Desktop org-wide Custom Connector.

### Non-goals

- Not a general-purpose MCP gateway.
- Not a replacement for first-party VS / VS Code support.
- Not a multi-tenant SaaS — single-tenant deployments only.
- No admin UI — operations are via Kusto + runbook.

### Reference

- https://learn.microsoft.com/en-us/azure/devops/mcp-server/remote-mcp-server
- https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp

## 2. High-level Architecture

```
 Claude Code / Desktop                 ADO MCP Bridge                   Microsoft
 ─────────────────────                 ─────────────────                ─────────
        │                                     │                            │
        │ 1. OAuth DCR + auth-code+PKCE       │                            │
        │────────────────────────────────────▶│                            │
        │                                     │ 2. auth-code+PKCE (cert)   │
        │                                     │───────────────────────────▶│  Entra
        │                                     │◀───────────────────────────│  (ADO scope)
        │ 3. Wrapper access/refresh tokens    │                            │
        │◀────────────────────────────────────│                            │
        │                                     │                            │
        │ 4. MCP traffic (Bearer wrapper)     │                            │
        │────────────────────────────────────▶│ 5. swap → ADO token        │
        │                                     │   (MSAL OBO/refresh)       │
        │                                     │───────────────────────────▶│  mcp.dev.azure.com
        │                                     │◀───────────────────────────│
        │◀────────────────────────────────────│ 6. response                │
```

Six components inside the bridge:

1. **OAuth AS endpoints** (`/.well-known/oauth-authorization-server`,
   `/register`, `/authorize`, `/token`, `/revoke`).
2. **Authorization controller / consent flow.**
3. **Token store** — Azure SQL Serverless, with Entra refresh tokens
   encrypted by a Key Vault DEK.
4. **Entra client** — MSAL.NET wrapper, single responsibility.
5. **MCP reverse proxy** — YARP-based, swaps wrapper bearer → Entra ADO
   token per request.
6. **Headers passthrough** — `X-MCP-Toolsets`, `X-MCP-Readonly`,
   `X-MCP-Tools`, `X-MCP-Insiders` flow through untouched.

Each Core class abstracts one external dep behind an interface:
`ITokenStore`, `IEntraTokenClient`, `IClock`, `IKeyVaultEncryptor`.

## 3. Stack & Repo Layout

- **.NET 10 LTS** (supported through Nov 2028).
- **ASP.NET Core** + **YARP** reverse proxy.
- **MSAL.NET** for confidential-client Entra calls (certificate auth).
- **EF Core** for the token store (real SQL, not in-memory provider).
- **Built-in `Microsoft.Extensions.Logging`** + **OpenTelemetry** →
  Application Insights. (Serilog explicitly rejected.)

```
AdoMcpBridge/
├── src/
│   ├── AdoMcpBridge.Api/        # ASP.NET Core host
│   └── AdoMcpBridge.Core/       # OAuth AS, Entra client, proxy logic; no ASP.NET deps
├── tests/
│   ├── AdoMcpBridge.Core.Tests/ # xUnit + NSubstitute (bulk of TDD)
│   ├── AdoMcpBridge.Api.Tests/  # WebApplicationFactory + WireMock.Net
│   └── AdoMcpBridge.Smoke/      # Nightly live smoke tests
├── infra/                       # Bicep
├── pipelines/                   # GitHub Actions workflows
└── docs/                        # Runbook, architecture, onboarding
```

## 4. Repo, Release & Distribution Model

- **GitHub** (not Azure Repos — chosen to enable community sharing).
- **GitHub Actions** for CI/CD.
- **Distribution: CI publishes a release.** Each tagged release ships:
  - Versioned container image to **GHCR**, signed with **Sigstore / cosign**.
  - Versioned **Bicep** templates.
  - Cross-platform **PowerShell 7+ `deploy.ps1`**.
- **Adopters deploy from a release, no fork required.** Updates:
  `git fetch --tags && git checkout vX.Y.Z && ./deploy.ps1`.
- Sample **reusable GHA workflow** ships under "Advanced" path for
  adopters who want their own CD pipeline.
- **SemVer**, `CHANGELOG.md`, `SECURITY.md` (private disclosure),
  `compatibility.md` mapping bridge versions → MS Remote MCP API
  generations.

## 5. Auth Model

- **Single shared Entra app registration**, admin-consented once.
  Per-user Graph DCR was considered and dropped — the wrapper is itself
  the OAuth AS facing Claude, so DCR is solved internally, and delegated
  tokens still carry real user identity into ADO.
- **Confidential client** + **certificate auth** (not client secret).
  Cert lives in Key Vault with auto-rotation.
- Delegated scopes: `https://mcp.dev.azure.com/Ado.Mcp.Tools`
  (resource app `2a72489c-aab2-4b65-b93a-a91edccf33b8`) + `openid` +
  `profile` + `offline_access`.
  *Corrected 2026-06-10:* the spec originally assumed the classic ADO
  scope (`499b84ac-1321-427f-aa17-267ca6975798/user_impersonation`),
  but the Remote MCP server is its own Entra resource and rejects
  ADO-audience tokens; `openid`/`profile` are required because the
  bridge reads `oid`/`preferred_username` from the id_token.
- **Single tenant.**

### End-to-end flow

1. Claude discovers the AS via
   `/.well-known/oauth-authorization-server`.
2. Claude registers itself dynamically via `/register` — DCR handled
   entirely inside the bridge, no Entra round-trip.
3. Claude initiates `/authorize` with PKCE (required — Claude is a
   public client).
4. Bridge kicks off **Entra auth-code + PKCE** flow on behalf of the
   user against Entra.
5. On callback, the bridge:
   - Validates state + PKCE.
   - Mints **opaque wrapper tokens**: 1h access, 14d refresh.
   - Persists the **encrypted Entra refresh token** keyed to the wrapper
     refresh token in the token store.
6. MCP traffic: middleware swaps the wrapper bearer → an Entra ADO
   access token via MSAL.NET (using the stored refresh token) and YARP
   forwards to `mcp.dev.azure.com` with `Authorization: Bearer <ado-token>`.
7. MCP headers (`X-MCP-Toolsets` / `-Readonly` / `-Tools` / `-Insiders`)
   pass through untouched.

### Security invariants

- The wrapper **never logs** tokens, codes, or PKCE verifiers. A Roslyn
  analyzer enforces this — any code that funnels these symbols into an
  `ILogger` call fails the build.
- Entra refresh tokens are encrypted at rest with a Key Vault-held DEK.
- All cross-component identifiers (state, codes, refresh tokens) are
  cryptographically random opaque values.

## 6. Azure Resources

| Resource | Purpose | Notes |
|---|---|---|
| **Azure Container Apps** | Bridge host | Public ingress + Entra auth at app layer; min replicas 0, max 5; HTTPS only. |
| **ACR (Basic)** | Image registry | Managed-identity pull, no admin user. |
| **Key Vault** | Entra app cert + DB DEK | System-assigned MI + RBAC. |
| **Azure SQL Database Serverless GP** | Token store | Three tables: `Clients`, `AuthorizationCodes`, `Tokens`. MI auth, no SQL passwords. EF Core migrations. |
| **App Insights + Log Analytics** | Observability | OpenTelemetry exporter. |

- **IP allowlisting:** off by default. Bicep parameter `allowedIpRanges`
  defaults to `["0.0.0.0/0"]`. README "Hardening" section documents how
  to populate with Anthropic + corp egress IPs.
- Per-env resource groups: `rg-adomcp-dev`, `rg-adomcp-prod`.
- Idle cost target: **< $20/mo**.

## 7. Testing Strategy

- **100% line/branch/method coverage target** on greenfield. Easier to
  maintain than an arbitrary threshold.
- Genuinely untestable code uses
  `[ExcludeFromCodeCoverage(Justification = "…")]`. A Roslyn analyzer
  flags any usage missing `Justification`.
- **Coverlet** enforces in CI:
  `/p:Threshold=100 /p:ThresholdType=line,branch,method`.
- **Unit tests** (xUnit + NSubstitute) cover Core. An in-memory
  `ITokenStore` (hand-written dict — *not* EF in-memory, which behaves
  differently from real SQL) backs unit-level OAuth flow tests.
- **Integration tests** via `WebApplicationFactory` + **WireMock.Net**
  for both Entra endpoints and `mcp.dev.azure.com`. MSAL is pointed at
  WireMock's base URL so we exercise real MSAL code paths against a
  fake Entra.
- **Smoke tests** (nightly + on tag) hit the real dev deployment and
  auto-open a GitHub Issue on failure.
- **No real wall-clock waits** — `IClock` everywhere.
- **No EF in-memory provider.** Integration tests use a real SQL
  container.
- Security-relevant tests tagged `[Trait("category", "security")]` so
  they can be filtered and reviewed as a set.
- **TDD** (red-green-refactor) for implementation.

## 8. Error Handling & Observability

### Error categories

| Category | HTTP | Body | Log level | Alerts? |
|---|---|---|---|---|
| **Caller error** | 400 | RFC 6749 OAuth error JSON | Info | No |
| **Upstream error** | 502 / mapped | Translated, never raw upstream | Warning | Rate-based |
| **Internal error** | 500 | Opaque (`error_id` only) | Error | Yes — any single occurrence |

### Correlation

- **W3C TraceContext** correlation-id middleware. The id is present in
  every log line, every outbound call, and every error response body.

### Telemetry

- **OpenTelemetry** → App Insights. Traces + custom metrics:
  - `oauth.dcr.registrations`
  - `oauth.token.issued` / `oauth.token.refreshed` / `oauth.token.rejected`
  - `entra.refresh.duration_ms`
  - `proxy.upstream.duration_ms` / `proxy.upstream.errors`
  - `proxy.in_flight`

### Alerts

- Any `internal_error` in 5 min.
- > 10% token rejections in 15 min.
- > 5% upstream errors in 15 min.
- Entra refresh p95 > 2s in 15 min.
- Cert < 14 days to expiry.

### Runbook

- Ships in repo. Six scenarios + saved Kusto queries.
- PR template enforces: **no new alert without a runbook entry.**

## 9. Claude Desktop Org Connector

The same deployed bridge serves both:

- **Claude Code** via `mcp.json` configuration.
- **Claude Desktop org-wide Custom Connector** registered by an org
  admin against the same `/authorize` + `/token` endpoints.

A tiny `/connector-info.json` endpoint serves connector card metadata.

## 10. Decisions Locked

These have been considered and decided; reopen only with a strong new
signal:

- **.NET 10**, not .NET 8.
- **GitHub + GitHub Actions**, not ADO + Pipelines.
- **One shared Entra app**, not per-user Graph registration.
- **Azure Container Apps**, not App Service / Functions / AKS.
- **Built-in `ILogger`**, not Serilog.
- **100% coverage** with `[ExcludeFromCodeCoverage(Justification=…)]`
  exceptions, not an arbitrary threshold.
- **CI-publishes-release** distribution model — not fork-and-deploy,
  not CI-only-no-CD.
- **No IP allowlist by default** — parameterised in Bicep, documented
  in README.

## 11. Open Questions

None blocking. Implementation plan to be produced by the
`writing-plans` skill once this spec is approved.
