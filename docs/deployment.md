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
> identity (step 8), and applying the EF Core schema migrations
> (step 8). The guide calls each out where it happens.

## What you end up with

One resource group containing (names shown for `prod`; `dev` follows
the same pattern):

| Resource | Name | Purpose |
|---|---|---|
| Container App | `ca-adomcp-prod` | The bridge itself (HTTPS ingress, scale 0–5) |
| Container Apps Environment | `cae-adomcp-prod` | Hosting environment |
| User-assigned managed identity | `id-adomcp-prod` | App identity for Key Vault, SQL, ACR |
| Key Vault | `kv-adomcp-prod` | Entra app certificate + token-encryption DEK |
| Azure SQL Serverless | `sql-adomcp-prod` / `sqldb-adomcp` | Token store (Entra-only auth, no SQL passwords) |
| ACR (Basic) | — | Provisioned for optional image import; releases pull from GHCR |
| App Insights + Log Analytics | — | OpenTelemetry traces, metrics, alerts |

Idle cost target is **under $20/month** (SQL serverless auto-pauses,
Container App scales to zero).

## Prerequisites

**Tools** (all cross-platform):

- [PowerShell 7+](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell)
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) (includes Bicep)
- [cosign](https://docs.sigstore.dev/cosign/system_config/installation/) — `deploy.ps1` refuses to deploy an image whose signature does not verify
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

# Look up the user_impersonation scope id on the Azure DevOps resource
$ADO_RESOURCE = '499b84ac-1321-427f-aa17-267ca6975798'
$SCOPE_ID = az ad sp show --id $ADO_RESOURCE `
  --query "oauth2PermissionScopes[?value=='user_impersonation'].id | [0]" -o tsv

# Request the delegated permission and grant tenant-wide admin consent
az ad app permission add --id $APP_ID `
  --api $ADO_RESOURCE --api-permissions "$SCOPE_ID=Scope"
az ad app permission admin-consent --id $APP_ID
```

Record the **app (client) id** (`$APP_ID`) and your **tenant id**
(`az account show --query tenantId -o tsv`) — you need both in step 4.

`offline_access` is requested at runtime as an OAuth scope; it needs no
API-permission entry. The redirect URI is added in step 7, once the
deployed hostname is known. The certificate is attached in step 6.

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
```

## 3. Create the resource group

```powershell
az group create --name rg-adomcp-prod --location uksouth
```

Any region works — but it must match `param location` in
`infra/main.prod.bicepparam` (default `uksouth`); edit that file if you
deploy elsewhere.

## 4. Check out a release and set parameters

```powershell
git clone https://github.com/a-teece/AdoMcpBridge.git && cd AdoMcpBridge
git fetch --tags && git checkout vX.Y.Z   # pick the latest release tag
```

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
./deploy.ps1 -Env prod -Tag vX.Y.Z `
    -SubscriptionId <subscription-guid> `
    -ResourceGroup rg-adomcp-prod
```

The script:

1. Runs `cosign verify` against `ghcr.io/a-teece/adomcpbridge:vX.Y.Z`,
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
known until the Container App exists. Edit your `.bicepparam` file:

```bicep
param issuerOverride = 'https://<fqdn-from-step-5>'
```

(or your custom domain, if you bind one) and re-run the same
`deploy.ps1` command from step 5. Keep this local edit — you'll reuse
the file for every upgrade.

## 7. Create the certificate and wire up the Entra app

The Bicep grants the managed identity access to a certificate named
`ado-mcp-bridge` in the Key Vault, but does not create it. You need the
**Key Vault Certificates Officer** role on the vault to do this
(`az role assignment create --role "Key Vault Certificates Officer" ...`).

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

Connect as a member of the SQL admin group (e.g.
`sqlcmd -S sql-adomcp-prod.database.windows.net -d sqldb-adomcp -G`) and
grant the managed identity access:

```sql
CREATE USER [id-adomcp-prod] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-adomcp-prod];
ALTER ROLE db_datawriter ADD MEMBER [id-adomcp-prod];
```

Apply the schema from your release checkout (uses your Entra login via
`Active Directory Default`):

```powershell
dotnet ef database update `
  --project src/AdoMcpBridge.Core --startup-project src/AdoMcpBridge.Api `
  --connection "Server=tcp:sql-adomcp-prod.database.windows.net,1433;Database=sqldb-adomcp;Authentication=Active Directory Default;Encrypt=True;"
```

Then remove the firewall rule and restart the app so it starts clean:

```powershell
az sql server firewall-rule delete -g rg-adomcp-prod -s sql-adomcp-prod -n temp-deploy
az containerapp revision restart -n ca-adomcp-prod -g rg-adomcp-prod `
  --revision (az containerapp revision list -n ca-adomcp-prod -g rg-adomcp-prod --query "[0].name" -o tsv)
```

## 9. Verify

```powershell
Invoke-RestMethod "https://$FQDN/healthz"                                  # → ok
Invoke-RestMethod "https://$FQDN/.well-known/oauth-authorization-server"   # → AS metadata with your issuer
Invoke-RestMethod "https://$FQDN/connector-info.json"                      # → connector card
```

All three returning 200 with your FQDN as the issuer means the bridge
is up. For a full end-to-end check (discovery → DCR → token →
`tools/list`), see [`docs/smoke-runbook.md`](smoke-runbook.md).

## 10. Connect clients

**Claude Code** — add the bridge as an HTTP MCP server:

```powershell
claude mcp add --transport http ado https://<fqdn>/mcp
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

## 11. Hardening

Ingress is **open to the internet by default**
(`allowedIpRanges = ['0.0.0.0/0']`). The OAuth layer protects the MCP
endpoints, but for production you should restrict ingress: populate
`allowedIpRanges` in `infra/main.prod.bicepparam` with
[Anthropic's egress IP ranges](https://docs.claude.com/en/api/ip-addresses)
plus your corporate egress IPs, and re-run `deploy.ps1`. The values are
applied as Container App ingress IP restrictions.

## 12. Upgrading to a new release

```powershell
git fetch --tags && git checkout vX.Y.Z
./deploy.ps1 -Env prod -Tag vX.Y.Z -SubscriptionId <sub> -ResourceGroup rg-adomcp-prod
```

Before upgrading, check [`CHANGELOG.md`](../CHANGELOG.md) and
[`compatibility.md`](../compatibility.md) (bridge version ↔ MS Remote
MCP API generation). If the release notes mention new database
migrations, repeat the `dotnet ef database update` part of step 8.
Your local `.bicepparam` edits (issuer, IP allowlist, region) are
uncommitted working-tree changes — `git checkout <tag>` preserves them
unless the file changed upstream, in which case re-apply them.

To roll back, deploy the previous tag the same way (unless the
changelog flagged an irreversible migration — see
[`docs/runbook.md`](runbook.md), scenario 6).

## 13. Automated CD from your own repo (advanced)

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

For operational incidents after go-live, [`docs/runbook.md`](runbook.md)
pairs every alert with a Kusto query and a procedure.
