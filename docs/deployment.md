# Deployment Guide — ADO MCP Bridge

This is the step-by-step guide for standing up your own instance of the
ADO MCP Bridge in your Azure subscription, from an empty resource group
to Claude Code / Claude Desktop connected and working.

It deploys from a published release — **no fork required**. Each
[release](https://github.com/a-teece/AdoMcpBridge/releases) ships a
cosign-signed container image on GHCR, versioned Bicep templates, an
SPDX SBOM, and `deploy.ps1`.

> **Heads-up on manual steps.** Three things cannot be done by the Bicep
> templates and are genuinely manual today: creating the Entra app
> certificate (step 6), creating the SQL contained user for the managed
> identity (step 8), and applying the EF Core schema migrations (step 8).
> The guide calls each out where it happens.

## What you end up with

One resource group containing (names shown for `prod`; `dev` follows
the same pattern):

| Resource | Name | Purpose |
|---|---|---|
| Container App | `ca-adomcp-prod` | The bridge itself (HTTPS ingress, scale 0–5) |
| Container Apps Environment | `cae-adomcp-prod` | Hosting environment |
| User-assigned managed identity | `id-adomcp-prod` | App identity for Key Vault, SQL, ACR, and blob storage |
| Key Vault | `kv-adomcp-prod` | Entra app certificate + token-encryption DEK |
| Azure SQL Serverless | `sql-adomcp-prod` / `sqldb-adomcp` | Token store (Entra-only auth, no SQL passwords) |
| Storage account | `stadomcpprod` | Short-lived upload slots for large ADO field writes (private, no account key) |
| ACR (Basic) | — | Provisioned for optional image import; releases pull from GHCR |
| App Insights + Log Analytics | — | OpenTelemetry traces, metrics, alerts |

Idle cost target is **under $20/month** (SQL serverless auto-pauses,
Container App scales to zero).

## Prerequisites

**Tools** (all cross-platform):

- [PowerShell 7+](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell)
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) (includes Bicep)
- [cosign](https://docs.sigstore.dev/cosign/system_config/installation/) — `deploy.ps1` refuses to deploy an image whose signature does not verify.
  On Windows, `winget install Sigstore.Cosign` installs the binary as
  `cosign-windows-amd64.exe` without creating a `cosign` alias; make a
  copy named `cosign.exe` next to it (the folder is already on `PATH`):

  ```powershell
  $pkg = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Sigstore.Cosign_Microsoft.Winget.Source_8wekyb3d8bbwe"
  Copy-Item "$pkg\cosign-windows-amd64.exe" "$pkg\cosign.exe"
  ```
- `git`
- For the one-time schema step: [.NET 10 SDK](https://dotnet.microsoft.com/download) and `dotnet tool install -g dotnet-ef`

**Access:**

- An Azure subscription where you hold **Owner** (or Contributor +
  User Access Administrator) on the target resource group — the Bicep
  creates RBAC role assignments, so Contributor alone is not enough.
- Entra permission to create an app registration and **grant
  tenant-wide admin consent** (Global Administrator or Privileged Role
  Administrator).
- An Azure DevOps organization whose Remote MCP Server
  (`https://mcp.dev.azure.com/{org}`) you want to reach.

The bridge is **single-tenant**: it serves users of one Entra tenant.

> **Shell note.** All CLI snippets below are **PowerShell 7** (which
> `deploy.ps1` requires anyway) and work identically on Windows, macOS,
> and Linux. If you prefer bash, the `az` commands are the same — only
> variable assignment (`VAR=$(az ...)` vs `$VAR = az ...`) and line
> continuation (`\` vs `` ` ``) differ.

## 1. Create the Entra app registration

The bridge authenticates to Entra as one shared, admin-consented
confidential client using **certificate auth** (no client secrets).

```powershell
# Create the app (single tenant)
$APP_ID = az ad app create `
  --display-name "ADO MCP Bridge" `
  --sign-in-audience AzureADMyOrg `
  --query appId -o tsv

# The Remote MCP server is its own Entra resource (NOT classic Azure
# DevOps): app 2a72489c-aab2-4b65-b93a-a91edccf33b8, delegated scope
# Ado.Mcp.Tools. Its service principal may not exist in your tenant
# yet — create it first (harmless if it already exists).
$MCP_RESOURCE = '2a72489c-aab2-4b65-b93a-a91edccf33b8'
az ad sp create --id $MCP_RESOURCE 2>$null

$SCOPE_ID = az ad sp show --id $MCP_RESOURCE `
  --query "oauth2PermissionScopes[?value=='Ado.Mcp.Tools'].id | [0]" -o tsv

# Request the delegated permission and grant tenant-wide admin consent
az ad app permission add --id $APP_ID `
  --api $MCP_RESOURCE --api-permissions "$SCOPE_ID=Scope"
az ad app permission admin-consent --id $APP_ID

# The native custom tools (ado_bridge_wit_get, ado_bridge_download_field,
# etc.) call the Azure DevOps REST API directly rather than through the
# MCP-server proxy, so they need a second delegated grant against the
# classic Azure DevOps resource. Unlike the MCP resource above, this one
# is a well-known first-party resource already present as a service
# principal in every tenant — no `az ad sp create` needed.
$ADO_RESOURCE = '499b84ac-1321-427f-aa17-267ca6975798'

$ADO_SCOPE_ID = az ad sp show --id $ADO_RESOURCE `
  --query "oauth2PermissionScopes[?value=='user_impersonation'].id | [0]" -o tsv

az ad app permission add --id $APP_ID `
  --api $ADO_RESOURCE --api-permissions "$ADO_SCOPE_ID=Scope"
az ad app permission admin-consent --id $APP_ID

# Print the two values you need later
"APP_ID    = $APP_ID"
"TENANT_ID = $(az account show --query tenantId -o tsv)"
```

Note the two values printed at the end — they go into the environment
variables in step 4.

**Verify the consent actually recorded** — `admin-consent` can silently
miss a permission added seconds earlier (propagation race). Confirm the
grant lists both `Ado.Mcp.Tools` and `user_impersonation`:

```powershell
$SP_ID = az ad sp show --id $APP_ID --query id -o tsv
az rest --method get `
  --url "https://graph.microsoft.com/v1.0/servicePrincipals/$SP_ID/oauth2PermissionGrants" `
  --query "value[].scope" -o tsv
```

If `Ado.Mcp.Tools` or `user_impersonation` is missing, re-run the
`admin-consent` line (or approve the consent prompt during the first
sign-in — as a tenant admin you can tick "Consent on behalf of your
organization").

`openid`, `profile`, and `offline_access` are requested at runtime as
OAuth scopes for both the MCP-proxy and native-tool token requests;
they need no API-permission entry. The redirect URI is added in step 7,
once the deployed hostname is known. The certificate is attached in
step 6.

## 2. Create the SQL admin security group

The SQL server is Entra-only (no SQL logins); its administrator is an
Entra security group. Create it and add yourself so you can run the
one-time schema step later:

```powershell
$SQL_ADMIN_OID = az ad group create `
  --display-name sg-adomcp-sqladmins-prod `
  --mail-nickname sg-adomcp-sqladmins-prod `
  --query id -o tsv

az ad group member add --group $SQL_ADMIN_OID `
  --member-id (az ad signed-in-user show --query id -o tsv)

# Print the value you need in step 4
"SQL_ADMIN_OID = $SQL_ADMIN_OID"
```

Note the printed object id — it goes into the environment variables in
step 4.

## 3. Create the resource group

```powershell
az group create --name rg-adomcp-prod --location uksouth
```

Any region works — but it must match `param location` in
`infra/main.prod.bicepparam` (default `uksouth`); edit that file if you
deploy elsewhere.

## 4. Check out a release and set parameters

```powershell
git clone https://github.com/a-teece/AdoMcpBridge.git
cd AdoMcpBridge
git fetch --tags

# Check out the latest release tag (or set $TAG = 'vX.Y.Z' to pin one)
$TAG = git tag --sort=-v:refname | Select-Object -First 1
git checkout $TAG
"Deploying release $TAG"
```

`$TAG` is reused by the deploy commands in steps 5 and 6, so run those
from this same shell session.

The per-environment parameter files
(`infra/main.dev.bicepparam` / `infra/main.prod.bicepparam`) read your
values from environment variables. Set them in the shell that will run
`deploy.ps1`:

```powershell
$env:ADOMCP_TENANT_ID          = "<tenant-guid>"
$env:ADOMCP_CLIENT_ID          = "<app-id-from-step-1>"
$env:ADOMCP_SQL_ADMIN_GROUP_OID = "<group-object-id-from-step-2>"
```

(`ADOMCP_IMAGE` exists in the param files but is overridden by
`deploy.ps1`, which constructs the image reference from `-Tag`.)

If you leave these unset the deployment proceeds with all-zero GUIDs
and the app cannot authenticate — double-check before deploying.

## 5. First deployment pass

```powershell
./deploy.ps1 -Env prod -Tag $TAG `
    -SubscriptionId <subscription-guid> `
    -ResourceGroup rg-adomcp-prod
```

The script:

1. Runs `cosign verify` against `ghcr.io/a-teece/adomcpbridge:<tag>`,
   pinned to this repo's `release.yml` workflow identity. It refuses to
   deploy if verification fails.
2. Runs `az deployment group create` with `infra/main.bicep` and your
   `.bicepparam` file.

If you forked the repo and publish your own images, pass
`-ImageOwner <your-gh-owner>` — verification then pins to *your*
release workflow identity.

When it finishes, capture the bridge's hostname:

```powershell
$FQDN = az containerapp show -n ca-adomcp-prod -g rg-adomcp-prod `
  --query properties.configuration.ingress.fqdn -o tsv
"https://$FQDN"
```

> The app is **not functional yet** — the OAuth issuer is a
> placeholder, the Entra certificate doesn't exist, and the database
> has no schema. Steps 6–8 fix that. A crash-looping container at this
> point is expected.

## 6. Pin the issuer (second deployment pass)

The bridge advertises its own URL as the OAuth issuer, which isn't
known until the Container App exists.

Open `infra\main.prod.bicepparam` (or `main.dev.bicepparam` for dev) in
your repo clone with any text editor — e.g.
`notepad infra\main.prod.bicepparam` — and change the existing
`issuerOverride` line from:

```bicep
param issuerOverride = ''
```

to your step-5 hostname (or your custom domain, if you bind one):

```bicep
param issuerOverride = 'https://<fqdn-from-step-5>'
```

Then re-run the same `deploy.ps1` command from step 5. Keep this local
edit — you'll reuse the file for every upgrade.

The file is tracked by git (not gitignored), but an uncommitted edit
never leaves your machine — nothing pushes unless you `git add` and
commit it. If you also develop in this clone and worry about sweeping
it into a commit with `git add -A`, hide it from `git status` with
`git update-index --skip-worktree infra/main.prod.bicepparam`. Nothing
in the file is sensitive either way: the tenant/client ids come from
environment variables and the issuer is your bridge's public URL.

## 7. Create the certificate and wire up the Entra app

The Bicep grants the managed identity access to a certificate named
`ado-mcp-bridge` in the Key Vault, but does not create it. The vault
uses RBAC, so even as subscription Owner you need an explicit data-plane
role — **Key Vault Certificates Officer** — or certificate operations
fail with `(Forbidden) Caller is not authorized`:

```powershell
az role assignment create `
  --role "Key Vault Certificates Officer" `
  --assignee (az ad signed-in-user show --query id -o tsv) `
  --scope (az keyvault show --name kv-adomcp-prod --query id -o tsv)
```

RBAC propagation can take a couple of minutes — if the next command
still returns Forbidden, wait and retry.

```powershell
# Create a self-signed cert with the default policy
# (written to a file — passing inline JSON to az from PowerShell is fragile)
az keyvault certificate get-default-policy | Out-File policy.json
az keyvault certificate create --vault-name kv-adomcp-prod `
  --name ado-mcp-bridge --policy '@policy.json'

# Download the public portion and attach it to the app registration
az keyvault certificate download --vault-name kv-adomcp-prod `
  --name ado-mcp-bridge --file ado-mcp-bridge.pem
az ad app credential reset --id $APP_ID --cert '@ado-mcp-bridge.pem' --append
Remove-Item ado-mcp-bridge.pem, policy.json
```

(The `@file` arguments are quoted so PowerShell doesn't parse `@` as its
splatting operator.)

A self-signed certificate is fine here — it authenticates your app to
Entra; no third party needs to trust it. The default policy auto-renews
in Key Vault, and an alert fires when the cert is within 14 days of
expiry (see [`docs/runbook.md`](runbook.md), scenario 5). **After any
rotation you must re-upload the new public key to the app registration**
— that part is not automatic.

Now add the redirect URI (the bridge's Entra callback):

```powershell
az ad app update --id $APP_ID `
  --web-redirect-uris "https://$FQDN/authorize/callback"
```

## 8. Create the database user and apply the schema

Bicep cannot create SQL contained users or run EF migrations — this is
a one-time manual step (re-run the migration part only when a release's
changelog says it ships new migrations).

The server firewall only allows Azure services, so temporarily allow
your own IP:

```powershell
$MYIP = Invoke-RestMethod https://api.ipify.org
az sql server firewall-rule create -g rg-adomcp-prod -s sql-adomcp-prod `
  -n temp-deploy --start-ip-address $MYIP --end-ip-address $MYIP
```

Grant the managed identity access using an Entra access token from your
`az` login (your SQL admin group membership from step 2 authorizes
this). Avoid `sqlcmd -G` — the classic ODBC build attempts Integrated
auth and fails with `0xCAA9001F ... Integrated Windows authentication
supported only in federation flow` on non-federated accounts:

```powershell
Install-Module SqlServer -Scope CurrentUser   # once, if not present

$token = az account get-access-token --resource https://database.windows.net/ `
  --query accessToken -o tsv
Invoke-Sqlcmd -ServerInstance sql-adomcp-prod.database.windows.net `
  -Database sqldb-adomcp -AccessToken $token -Query @"
CREATE USER [id-adomcp-prod] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-adomcp-prod];
ALTER ROLE db_datawriter ADD MEMBER [id-adomcp-prod];
"@
```

Apply the schema from your release checkout. EF's design-time factory
uses a placeholder connection, so generate the idempotent script and
run it over the same token-authenticated connection:

```powershell
dotnet restore
dotnet ef migrations script --idempotent `
  --project src/AdoMcpBridge.Core --startup-project src/AdoMcpBridge.Core `
  --output migrate.sql

Invoke-Sqlcmd -ServerInstance sql-adomcp-prod.database.windows.net `
  -Database sqldb-adomcp -AccessToken $token -InputFile migrate.sql
Remove-Item migrate.sql
```

(If `dotnet ef` is missing: `dotnet tool install -g dotnet-ef`. Tokens
expire after ~1 hour — re-run the `get-access-token` line if
`Invoke-Sqlcmd` reports an authentication failure.)

Then remove the firewall rule and restart the app so it starts clean:

```powershell
az sql server firewall-rule delete -g rg-adomcp-prod -s sql-adomcp-prod -n temp-deploy
az containerapp revision restart -n ca-adomcp-prod -g rg-adomcp-prod `
  --revision (az containerapp revision list -n ca-adomcp-prod -g rg-adomcp-prod --query "[0].name" -o tsv)
```

## 9. No Azure DevOps organisation setup needed for native tools

The native custom tools (`ado_bridge_wit_get`,
`ado_bridge_wit_get_batch`, `ado_bridge_download_field`,
`ado_bridge_create_upload_slot`, `ado_bridge_write_field_from_slot`)
call the Azure DevOps REST API authenticated as **the signed-in end
user**, using the second delegated grant set up in step 1 — not as the
bridge's managed identity. There is no service-identity provisioning
step here: each user's own existing Azure DevOps permissions apply, the
same as if they'd called the REST API directly.

> **What permissions does the signed-in user need?**
> - `ado_bridge_wit_get` / `ado_bridge_wit_get_batch` /
>   `ado_bridge_download_field` — `GET /_apis/wit/workitems/{id}?fields=...`
>   requires "View work items in this node" (read-only) in the relevant
>   project/area.
> - `ado_bridge_create_upload_slot` / `ado_bridge_write_field_from_slot` —
>   `PATCH /_apis/wit/workitems/{id}` requires "Edit work items in this
>   node".

Nothing beyond a normal ADO user's permissions needs configuring per
user; there's no group membership or org-level user add to perform.

**Manual sanity check (optional).** To confirm the Entra scope from
step 1 is wired up correctly without going through Claude, request a
token for the classic ADO resource against your own signed-in account:

```powershell
# Manual Entra-scope sanity check only — this exercises your own
# az-login identity, not the bridge's delegated-token flow. It does not
# verify end-to-end behaviour; a real Claude tool call against the
# deployed bridge is the actual verification (it will 401 cleanly if the
# signed-in user lacks ADO permissions).
$TOKEN = az account get-access-token `
  --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv
Invoke-RestMethod `
  -Uri "https://dev.azure.com/{org}/{project}/_apis/wit/workitems/1?api-version=7.1" `
  -Headers @{ Authorization = "Bearer $TOKEN" }
```

## 10. Verify

```powershell
Invoke-RestMethod "https://$FQDN/healthz"                                  # → ok
Invoke-RestMethod "https://$FQDN/.well-known/oauth-authorization-server"   # → AS metadata with your issuer
Invoke-RestMethod "https://$FQDN/connector-info.json"                      # → connector card
```

All three returning 200 with your FQDN as the issuer means the bridge
is up. For a full end-to-end check (discovery → DCR → token →
`tools/list`), see [`docs/smoke-runbook.md`](smoke-runbook.md).

## 11. Connect clients

**Claude Code** — add the bridge as an HTTP MCP server. The URL must
include your Azure DevOps **organization name** (whatever follows
`https://dev.azure.com/`) — Microsoft's MCP server lives at
`https://mcp.dev.azure.com/{org}` and a bare `/mcp` proxies to a 401:

```powershell
claude mcp add --transport http ado https://<fqdn>/mcp/<your-ado-org>
```

OAuth is discovered automatically via
`/.well-known/oauth-authorization-server`; on first use Claude Code
opens a browser for the Entra sign-in + consent flow. The MCP
behavioral headers (`X-MCP-Toolsets`, `X-MCP-Readonly`, `X-MCP-Tools`,
`X-MCP-Insiders`) pass through to Microsoft's server untouched.

**Claude Desktop (org-wide Custom Connector)** — a Claude org admin
registers a Custom Connector pointing at the bridge URL; the
`/connector-info.json` card supplies the metadata. See
[Anthropic's custom-connector guide](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp).

Each user signs in with their own Entra account — their real identity
flows through to Azure DevOps, so existing ADO permissions apply.

## 12. Hardening

Ingress is **open to the internet by default**
(`allowedIpRanges = ['0.0.0.0/0']`). The OAuth layer protects the MCP
endpoints, but for production you should restrict ingress: populate
`allowedIpRanges` in `infra/main.prod.bicepparam` with
[Anthropic's egress IP ranges](https://docs.claude.com/en/api/ip-addresses)
plus your corporate egress IPs, and re-run `deploy.ps1`. The values are
applied as Container App ingress IP restrictions.

## 13. Upgrading to a new release

```powershell
git fetch --tags
$TAG = git tag --sort=-v:refname | Select-Object -First 1
git checkout $TAG
./deploy.ps1 -Env prod -Tag $TAG -SubscriptionId <sub> -ResourceGroup rg-adomcp-prod
```

Before upgrading, check [`CHANGELOG.md`](../CHANGELOG.md) and
[`compatibility.md`](../compatibility.md) (bridge version ↔ MS Remote
MCP API generation). If the release notes mention new database
migrations, repeat the migration-script part of step 8.
Your local `.bicepparam` edits (issuer, IP allowlist, region) are
uncommitted working-tree changes — `git checkout <tag>` preserves them
unless the file changed upstream, in which case re-apply them.

To roll back, deploy the previous tag the same way (unless the
changelog flagged an irreversible migration — see
[`docs/runbook.md`](runbook.md), scenario 6).

## 14. Automated CD from your own repo (advanced)

If you'd rather deploy via GitHub Actions than a workstation, this repo
ships a reusable workflow
([`.github/workflows/reusable-cd.yml`](../.github/workflows/reusable-cd.yml))
that does OIDC federated login and runs `deploy.ps1` — no long-lived
Azure credentials in your repo.

1. Create an Entra app registration for the deployer, grant it
   **Owner** on the resource group, and add a federated credential with
   issuer `https://token.actions.githubusercontent.com` and subject
   `repo:<your-owner>/<your-repo>:environment:prod`.
2. In your repo, create a `prod` environment and an `AZURE_CLIENT_ID`
   secret holding the deployer app's client id.
3. Add a workflow that calls the reusable one — a ready-to-adapt
   example lives at
   [`docs/adopters/example-consumer.yml`](adopters/example-consumer.yml).

The first-time setup steps (1–4 and 6–8) remain manual either way.

## Troubleshooting first deploys

| Symptom | Likely cause |
|---|---|
| `cosign verify failed` | Tag doesn't exist on GHCR yet, or you deployed a fork's image without `-ImageOwner` |
| `Parameter file not found` | Running `deploy.ps1` from outside the repo checkout |
| Deployment fails on role assignments | Deploying principal lacks Owner / User Access Administrator on the RG |
| Container crash-loops after step 5 | Expected until steps 6–8 are done; check `az containerapp logs show` afterwards |
| `Login failed for user '<token-identified principal>'` in logs | Step 8 contained-user grant missing or wrong MI name |
| OAuth flow redirects fail | Redirect URI in the app registration doesn't exactly match `https://<fqdn>/authorize/callback`, or `issuerOverride` still unset |
| Claude: `Protected resource https://mcp.dev.azure.com/... does not match expected ...` | Upstream 401 leaked through the proxy: MCP URL missing the `/<org>` segment, or the `Ado.Mcp.Tools` grant is missing (see step 1 verification). Restart Claude Code after fixing — it caches the failure |
| Users see an Entra consent prompt despite admin consent | The step 1 consent race — re-run `az ad app permission admin-consent` and verify the grant |
| `ado_bridge_download_field` or `ado_bridge_write_field_from_slot` returns 401/403 | Signed-in user lacks the required work-item permission in the target project, or the `user_impersonation` grant is missing (see step 1 verification) — see step 9 |
| `ado_bridge_write_field_from_slot` returns 403 on PATCH | Signed-in user has read-only access; they need "Edit work items in this node" in the target project/area |

For operational incidents after go-live, [`docs/runbook.md`](runbook.md)
pairs every alert with a Kusto query and a procedure.
