## Summary
<!-- 1-3 bullets. Why this change exists. -->

## Review focus
<!-- Where reviewers should look hardest. -->

## Test plan
- [ ] `dotnet test` passes locally with 100% coverage.
- [ ] `dotnet format --verify-no-changes` passes.
- [ ] If this PR adds or modifies an alert in `infra/modules/observability.bicep` or `infra/alerts/`, it adds a matching `### ` entry to `docs/runbook.md`. (Enforced by `pr-template-check.yml`.)

## Compatibility
<!-- Does this change the bridge ↔ MS Remote MCP API mapping?
     If yes, update compatibility.md. -->
