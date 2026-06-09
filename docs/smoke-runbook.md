# Smoke Test Runbook

The `AdoMcpBridge.Smoke` project exercises a deployed bridge end-to-end
(discovery → connector card → token refresh → MCP `tools/list`). It runs
nightly via `.github/workflows/smoke.yml` and on every published release.

## Required environment variables

| Variable | Source | Purpose |
|---|---|---|
| `ADOMCP_BRIDGE_URL` | GitHub Actions var (per environment) | Absolute base URL of the deployed bridge, e.g. `https://ca-adomcp-dev.<region>.azurecontainerapps.io`. |
| `ADOMCP_SMOKE_REFRESH_TOKEN` | GitHub Actions secret | Pre-provisioned wrapper refresh token for the dedicated smoke test user. |
| `ADOMCP_SMOKE_CLIENT_ID` | GitHub Actions secret | `client_id` of the DCR record registered for the smoke test user. |

When these are unset, the live tests are skipped (`SkippableFact`) so the
solution-wide `dotnet test` in `ci.yml` stays green; only the redaction
unit tests run there.

## Running locally against dev

1. Get the dev URL from the deploy job output or `vars.ADOMCP_BRIDGE_URL`
   in the repo settings.
2. Obtain a smoke refresh token (see next section). Do **not** reuse your
   personal user's token — the token store binds it to a real Entra user
   and the smoke job would impersonate you.
3. Run:

   ```bash
   export ADOMCP_BRIDGE_URL="https://ca-adomcp-dev.<region>.azurecontainerapps.io"
   export ADOMCP_SMOKE_REFRESH_TOKEN="$(az keyvault secret show \
     --vault-name kv-adomcp-dev \
     --name smoke-refresh-token \
     --query value -o tsv)"
   export ADOMCP_SMOKE_CLIENT_ID="$(az keyvault secret show \
     --vault-name kv-adomcp-dev \
     --name smoke-client-id \
     --query value -o tsv)"

   dotnet test tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj \
     --filter "Category=smoke" \
     --logger "console;verbosity=detailed"
   ```

4. Unset the variables when you're done:

   ```bash
   unset ADOMCP_SMOKE_REFRESH_TOKEN ADOMCP_SMOKE_CLIENT_ID ADOMCP_BRIDGE_URL
   ```

## Obtaining a smoke refresh token without leaking real user data

The smoke flow uses a **dedicated Entra test user** in the same tenant,
with no production ADO project membership beyond a single read-only test
project. Procedure:

1. As tenant admin, create user `smoke-bot@<tenant>.onmicrosoft.com`.
2. Grant the user reader-only access to a single throwaway ADO project.
3. From a clean browser profile, sign in to Claude Code against the dev
   bridge as that user, complete consent.
4. Once the bridge has minted wrapper tokens, dump the **wrapper refresh
   token** (the opaque base64url value from the `/token` response — not
   the Entra refresh token). The OAuth flow surfaces it to the OAuth
   client (Claude Code). Capture it from Claude Code's MCP debug log on
   a developer machine.
5. Store the value in Key Vault `kv-adomcp-dev` as secret
   `smoke-refresh-token`, and the registered DCR `client_id` as
   `smoke-client-id`. Mirror both into GitHub Actions secrets.

Rotate the smoke token quarterly and immediately if it is ever exposed
in plaintext. The smoke tests never log token values — they only emit a
`<redacted len=N>` sentinel via `SmokeEnvironment.Redact`.

## Triage on failure

The workflow opens a GitHub Issue labelled `smoke-failure` via the
`auto-open-issue` composite action. The action dedups by exact title;
re-runs comment on the existing issue instead of opening duplicates.
Each comment includes the workflow run URL and commit SHA.
