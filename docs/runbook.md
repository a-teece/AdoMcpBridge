# ADO MCP Bridge Runbook

Every alert in `infra/modules/alerts.bicep` has a corresponding scenario
below. PRs that add new alerts must add a new scenario (see PR template).

All Kusto queries assume the App Insights workspace bound to
`appi-adomcp-{env}`. Replace time windows as needed.

---

### Scenario 1 — Internal error spike

**Alert:** `adomcp-{env}-internal-error` — any single `internal_error` in 5 min.

**Symptom:** Bridge returned HTTP 500 with `error_id` in body; users see
"An internal error occurred." Severity 1 page.

**Saved Kusto query:**

```kusto
AppTraces
| where TimeGenerated > ago(1h)
| where SeverityLevel == 3
| where Message has "internal_error"
| extend error_id = tostring(Properties.error_id)
| extend correlation_id = tostring(Properties.correlation_id)
| project TimeGenerated, error_id, correlation_id, Message, OperationName
| order by TimeGenerated desc
```

**Triage steps:**
1. Capture the `error_id` from the alert payload (or first row of the
   query above).
2. Pivot to the full exception via `AppExceptions | where
   Properties.error_id == "<error_id>"` to see the stack and inner
   exception type.
3. Pivot to the request via the `correlation_id` against `AppRequests`
   to identify endpoint and client.
4. Check `AppDependencies` filtered to the same `OperationId` for an
   upstream failure (SQL, Key Vault, Entra) that should have surfaced
   as `UpstreamErrorException`.

**Mitigation:**
- If the root cause is a transient dependency outage, monitor the
  alert; it auto-resolves.
- If a code defect: open a hotfix PR, follow CI-publishes-release flow,
  redeploy the new tag. There is no admin UI; restart via
  `az containerapp revision restart` only as a last resort.

**Escalation:** Page on-call after 3 occurrences in 15 min or any
occurrence affecting >5 distinct `correlation_id`s.

---

### Scenario 2 — Token rejection spike

**Alert:** `adomcp-{env}-token-rejection-rate` — >10% token rejections
in 15 min.

**Symptom:** Clients see repeated 401s from `/mcp/*`; `oauth.token.rejected`
counter climbs.

**Saved Kusto query:**

```kusto
let window = 15m;
let rejected = AppMetrics
    | where TimeGenerated > ago(window)
    | where Name == "oauth.token.rejected"
    | summarize r = sum(ValueSum) by Reason = tostring(Properties.reason);
let issued = AppMetrics
    | where TimeGenerated > ago(window)
    | where Name == "oauth.token.issued"
    | summarize i = sum(ValueSum);
rejected
| extend total_issued = toscalar(issued)
| extend reject_pct = round(100.0 * r / (r + total_issued), 2)
| order by r desc
```

**Triage steps:**
1. Identify the dominant `reason` tag — typical values: `expired`,
   `not_found`, `revoked`, `signature_mismatch`.
2. If `expired` dominates, confirm clock skew between Container App
   replicas using `Heartbeat` in Log Analytics.
3. If `not_found` dominates, check for a recent token-store migration
   or rollback — query `AppTraces | where Message has "EF migration"`.
4. If `revoked` dominates, look for a security incident — review
   `/revoke` calls: `AppRequests | where Url has "/revoke" | summarize
   count() by ClientIP_s`.

**Mitigation:**
- For expiry-driven floods, no action — clients will refresh.
- For schema/migration causes, roll back to the previous tag via
  `git checkout <prev-tag> && ./deploy.ps1`.
- For suspected attack, populate `allowedIpRanges` in the Bicep
  parameter file and redeploy.

**Escalation:** Page on-call if rejection rate stays above 25% for
30 min or if `revoked` reason exceeds 50 events in 15 min.

---

### Scenario 3 — Upstream MCP failures

**Alert:** `adomcp-{env}-upstream-error-rate` — >5% upstream errors
in 15 min.

**Symptom:** YARP proxy returns 502/503/504; `proxy.upstream.errors`
climbs.

**Saved Kusto query:**

```kusto
let window = 15m;
AppMetrics
| where TimeGenerated > ago(window)
| where Name == "proxy.upstream.errors"
| extend status = tostring(Properties.status_code)
| summarize errors = sum(ValueSum) by bin(TimeGenerated, 1m), status
| order by TimeGenerated desc
```

**Triage steps:**
1. Check the Microsoft Azure status page for `mcp.dev.azure.com`.
2. Run `AppDependencies | where Target == "mcp.dev.azure.com" |
   summarize count(), avg(DurationMs) by ResultCode, bin(TimeGenerated, 1m)`
   to confirm the failure originates upstream, not in our middleware.
3. Confirm Entra token swap is healthy via the Scenario 4 query — a
   stale ADO token presents as upstream 401, not as `entra.refresh`
   latency.
4. Sample three failing `correlation_id`s and walk the trace end-to-end
   in the App Insights transaction view.

**Mitigation:**
- Transient upstream: no action; alert auto-resolves.
- Auth-related 401 surge from upstream: rotate the Entra cert via the
  `key-vault-rotation` GitHub workflow.
- Sustained outage: post status to the GitHub Discussions board and
  set Container App min replicas to 0 to fail closed cleanly.

**Escalation:** Page on-call if error rate exceeds 20% for 15 min or
all upstream calls fail for 5 consecutive minutes.

---

### Scenario 4 — Slow Entra refresh

**Alert:** `adomcp-{env}-entra-refresh-p95` — `entra.refresh.duration_ms`
p95 > 2s in 15 min.

**Symptom:** First MCP request after token refresh feels sluggish;
client timeouts in Claude Desktop.

**Saved Kusto query:**

```kusto
let window = 15m;
AppMetrics
| where TimeGenerated > ago(window)
| where Name == "entra.refresh.duration_ms"
| summarize p50 = percentile(ValueSum, 50),
            p95 = percentile(ValueSum, 95),
            p99 = percentile(ValueSum, 99),
            count_ = count()
            by bin(TimeGenerated, 1m)
| order by TimeGenerated desc
```

**Triage steps:**
1. Compare p95 against the last 24h baseline using the same query with
   `ago(24h)` — sustained drift indicates a real regression.
2. Inspect `AppDependencies | where Target has "login.microsoftonline.com"
   | summarize p95 = percentile(DurationMs, 95) by bin(TimeGenerated, 1m)`
   — if Microsoft-side p95 is also elevated, this is upstream latency,
   not ours.
3. Check Container App CPU/memory — saturated replicas serialise MSAL
   cert signing.
4. Verify the Key Vault cert hasn't started rotating mid-window —
   `AzureDiagnostics | where ResourceProvider == "MICROSOFT.KEYVAULT"
   | where OperationName has "Certificate"`.

**Mitigation:**
- If saturation: bump `maxReplicas` parameter and redeploy.
- If Entra-side latency: no action; alert auto-resolves.
- If cert-rotation correlated: confirm rotation completes and the
  Container App revision picks up the new cert (restart revision if
  not).

**Escalation:** Page on-call if p95 > 5s for 30 min or any p99 > 10s.

---

### Scenario 5 — Certificate near expiry

**Alert:** `adomcp-{env}-cert-expiry` — Key Vault emits the Event Grid
`Microsoft.KeyVault.CertificateNearExpiry` event ~30 days before a
certificate expires; the event subscription's MonitorAlert destination
raises a Sev2 Azure Monitor alert on the action group.

**Symptom:** Pre-failure warning. If ignored, Entra auth will hard-fail
when the cert expires.

**Check remaining validity directly:**

```bash
az keyvault certificate show --vault-name kv-adomcp-{env} \
  --name ado-mcp-bridge --query "attributes.expires" -o tsv
```

**Triage steps:**
1. Identify the offending cert from the alert resource id.
2. Confirm auto-rotation policy is configured:
   `az keyvault certificate show --vault-name kv-adomcp-{env}
   --name ado-mcp-bridge --query "policy.lifetimeActions"`.
3. If auto-rotation is enabled, verify the Key Vault MI has
   `Certificates Officer` role on itself (rotation requires it).
4. If auto-rotation is disabled (legacy deployments), trigger manual
   rotation via the `key-vault-rotation` workflow.

**Mitigation:**
- Run `gh workflow run key-vault-rotation.yml -f env={env}` which
  issues a new cert, uploads it to Key Vault under the same name, and
  triggers a Container App revision restart to pick it up.
- After rotation, re-run the expiry check above and confirm the new
  expiry date is > 60 days out.

**Escalation:** Page on-call when fewer than 3 days remain.

---

### Scenario 6 — Database outage / migration failure

**Alert:** Triggered indirectly — internal-error alert (Scenario 1)
fires when SQL is unreachable; migration failures surface in startup
logs and fail the CI deploy job.

**Symptom:** All OAuth endpoints 500; `/mcp/*` returns 500 because
bearer lookup against `Tokens` table fails.

**Saved Kusto query:**

```kusto
union
    (AppDependencies
        | where TimeGenerated > ago(1h)
        | where Type == "SQL"
        | where Success == false
        | project TimeGenerated, Target, ResultCode, DurationMs, Message = Properties.exceptionMessage),
    (AppTraces
        | where TimeGenerated > ago(1h)
        | where Message has "EF Core" or Message has "migration"
        | project TimeGenerated, Message, SeverityLevel)
| order by TimeGenerated desc
```

**Triage steps:**
1. Check SQL server status: `az sql db show-connection-string` then
   `sqlcmd -S sql-adomcp-{env}.database.windows.net -d sqldb-adomcp
   -G -Q "SELECT 1"`.
2. Confirm the Container App's user-assigned MI still has
   `db_datareader` + `db_datawriter` + EF migration role on
   `sqldb-adomcp`.
3. If a CI deploy failed during the migration step, inspect the GHA
   job logs for the `dotnet ef database update` step and capture the
   first SQL error.
4. If outage is region-wide, check Azure status page for the SQL
   region.

**Mitigation:**
- **Outage:** SQL Serverless will auto-resume on the next request — if
  it is stuck cold, run a probe query manually to wake it.
- **Migration failure:** Roll forward only if the migration is
  idempotent. Otherwise, redeploy the previous tag and open a hotfix
  PR with a corrected migration. Never edit a shipped migration —
  always add a new one.
- **Permission drift:** Re-run the `assign-sql-roles` workflow to
  restore MI grants.

**Escalation:** Page on-call immediately — all bridge functionality is
unavailable. Post status to the GitHub Discussions board.

---
