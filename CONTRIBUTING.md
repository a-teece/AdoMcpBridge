# Contributing to ADO MCP Bridge

Thanks for your interest. This project is an open-source community fix
for a Microsoft-acknowledged gap in the Remote Azure DevOps MCP Server
— specifically, that non-VS/VS-Code MCP clients (Claude Code, Claude
Desktop, etc.) can't yet perform OAuth dynamic client registration
against Entra.

## Branching & PR workflow

- `main` is **protected**. No direct pushes.
- All changes go on a feature branch and merge via PR.
- Branch naming:
  - `feat/<short-kebab-description>` — new functionality
  - `fix/<short-kebab-description>` — bug fixes
  - `docs/<short-kebab-description>` — docs only
  - `chore/<short-kebab-description>` — tooling, deps, infra plumbing
  - `claude/<short-kebab-description>` — branches authored by Claude
    sessions
- Keep one logical change per PR. Split if scope grows.
- Rebase onto the latest `main` before opening or updating a PR.
  Use `--force-with-lease` for force-pushes on your own branch.

## Commit messages

Conventional Commits style:

```
<type>: <imperative subject ≤ 72 chars>

<optional body — explain WHY, not WHAT>
```

Types: `feat`, `fix`, `docs`, `chore`, `test`, `refactor`, `perf`,
`build`, `ci`, `style`.

## Pull request expectations

Every PR body must contain:

- `## Summary` — 1-3 bullets, what changed and why.
- `## Review focus` — where the reviewer should concentrate.
- `## Test plan` — bulleted checklist (manual + automated) proving the
  change works.

PRs that add an alert without a runbook entry, or add new code without
tests at the project's coverage target, will be rejected.

## Development quality bar

- **TDD** — red, green, refactor.
- **100% line/branch/method coverage** on greenfield code. Use
  `[ExcludeFromCodeCoverage(Justification = "…")]` only where the code
  is genuinely untestable; the `Justification` is mandatory and is
  enforced by a Roslyn analyzer.
- **No EF in-memory provider** in tests — integration tests run against
  real SQL.
- **Never log tokens, codes, or PKCE verifiers.** Roslyn analyzer
  enforces.
- Prefer the built-in Microsoft stack over third-party libraries where
  capability is equivalent (e.g. `Microsoft.Extensions.Logging` over
  Serilog).

## Reporting security issues

Use GitHub's private vulnerability reporting via the repository's
**Security** tab. Do **not** open public issues for suspected
vulnerabilities.

## Where to start

If you want to contribute but don't know where to begin:

1. Read the design spec:
   [`docs/specs/2026-06-09-ado-mcp-bridge-design.md`](docs/specs/2026-06-09-ado-mcp-bridge-design.md).
2. Look at open issues labelled `good first issue`.
3. Open a draft PR early if you're unsure about direction — feedback is
   cheaper before code than after.
