# ADO MCP Bridge

[![ci](https://github.com/a-teece/AdoMcpBridge/actions/workflows/ci.yml/badge.svg)](https://github.com/a-teece/AdoMcpBridge/actions/workflows/ci.yml)

A C# service that wraps Microsoft's [Remote Azure DevOps MCP Server](https://learn.microsoft.com/en-us/azure/devops/mcp-server/remote-mcp-server)
so that **Claude Code**, **Claude Desktop**, and other MCP clients that
don't yet support Entra dynamic client registration can use it today.

The bridge acts as an OAuth **Authorization Server** toward the client
(DCR, auth-code + PKCE, opaque wrapper tokens) and as a **static,
admin-consented confidential client** toward Entra (certificate auth).
Per request, it swaps the wrapper bearer for a delegated Entra ADO
token and proxies to `mcp.dev.azure.com` via YARP — so real user
identity flows through to Azure DevOps.

## Endpoints

| Path | Purpose |
|---|---|
| `/.well-known/oauth-authorization-server` | OAuth AS metadata (discovery) |
| `/register`, `/authorize`, `/token`, `/revoke` | RFC 7591 DCR + auth-code/PKCE + rotating refresh + RFC 7009 |
| `/mcp/{**}` | Authenticated reverse proxy to the Remote ADO MCP server |
| `/connector-info.json` | Claude Desktop org-connector card |
| `/healthz` | Liveness |

## Deploy from a release

**Full step-by-step guide: [`docs/deployment.md`](docs/deployment.md)** —
covers the Entra app registration, certificate, database setup, client
connection, hardening, and upgrades.

Each [release](https://github.com/a-teece/AdoMcpBridge/releases) ships a
cosign-signed container image on GHCR, a versioned Bicep bundle, an SPDX
SBOM, and `deploy.ps1`. No fork required:

```bash
git fetch --tags && git checkout vX.Y.Z
pwsh ./deploy.ps1 -Env dev -Tag vX.Y.Z `
    -SubscriptionId <subscription-guid> `
    -ResourceGroup rg-adomcp-dev
```

Prerequisites: PowerShell 7+, Azure CLI, [cosign](https://docs.sigstore.dev/cosign/system_config/installation/)
(the script verifies the image signature before deploying), an Entra app
registration with certificate auth (cert named `ado-mcp-bridge` in the
deployed Key Vault) admin-consented for the Remote MCP server's
delegated scope `Ado.Mcp.Tools` (resource app
`2a72489c-aab2-4b65-b93a-a91edccf33b8` — *not* the classic ADO
`499b84ac.../user_impersonation` scope, which the MCP server rejects),
and an AAD group for SQL admin. Per-environment values
are supplied via environment variables read by
`infra/main.{env}.bicepparam` (`ADOMCP_TENANT_ID`, `ADOMCP_CLIENT_ID`,
`ADOMCP_IMAGE`, `ADOMCP_SQL_ADMIN_GROUP_OID`).

Verify a release image out-of-band:

```bash
cosign verify \
  --certificate-identity-regexp "^https://github\.com/a-teece/AdoMcpBridge/\.github/workflows/release\.yml@refs/tags/" \
  --certificate-oidc-issuer 'https://token.actions.githubusercontent.com' \
  ghcr.io/a-teece/adomcpbridge:vX.Y.Z
```

## Hardening

Ingress is **open by default** (`allowedIpRanges = ['0.0.0.0/0']`).
For production, populate `allowedIpRanges` in
`infra/main.prod.bicepparam` with Anthropic egress ranges plus your
corporate egress IPs and redeploy — the Container App then applies them
as ingress IP restrictions.

## Connect a client

- **Claude Code** — add an MCP server pointing at
  `https://<your-bridge-fqdn>/mcp/<your-ado-org>` (the org segment is
  required — Microsoft's server lives at `mcp.dev.azure.com/{org}`);
  OAuth is discovered automatically via
  `/.well-known/oauth-authorization-server`.
- **Claude Desktop (org connector)** — an org admin registers a Custom
  Connector against the same `/authorize` + `/token` endpoints;
  `/connector-info.json` serves the connector card metadata.

## Operations

- [`docs/deployment.md`](docs/deployment.md) — step-by-step deployment
  guide from empty resource group to connected client.
- [`docs/runbook.md`](docs/runbook.md) — six alert-paired scenarios with
  executable Kusto queries.
- [`docs/smoke-runbook.md`](docs/smoke-runbook.md) — nightly smoke tests
  against the live deployment.
- [`compatibility.md`](compatibility.md) — bridge version ↔ MS Remote
  MCP API generation matrix.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Design rationale lives in
[`docs/specs/2026-06-09-ado-mcp-bridge-design.md`](docs/specs/2026-06-09-ado-mcp-bridge-design.md).
Security issues: use GitHub's private vulnerability reporting (the
**Security** tab) — never a public issue.

## License

[MIT](LICENSE).
