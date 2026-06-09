# CLAUDE.md — Instructions for Claude sessions in this repo

## Branching policy (MANDATORY)

- **Never commit or push to `main`.** `main` is protected and only
  advances via merged pull requests.
- Every change — code, docs, infra, CI config — lands on a feature
  branch and ships via a PR.
- Branch from the latest `main`:
  ```
  git checkout main && git pull origin main
  git checkout -b claude/<short-kebab-description>
  ```
- One logical change per branch / PR. If scope grows mid-task, stop
  and split before pushing.
- After pushing, **always open a PR** (ready for review, not draft) and
  link it back in the reply.
- Do not force-push to `main`. Force-pushing your own feature branch is
  fine when needed (use `--force-with-lease`).

## Commit & PR style

- Commit messages: Conventional-Commits style prefix
  (`docs:`, `feat:`, `fix:`, `chore:`, `test:`, `refactor:`).
- Subject ≤ 72 chars; body explains the *why*, not the *what*.
- PR title mirrors the commit subject. PR body uses
  `## Summary` / `## Review focus` / `## Test plan` sections.

## Project context

This repo is the **ADO MCP Bridge** — a C# service wrapping Microsoft's
Remote Azure DevOps MCP Server so that Claude Code / Claude Desktop and
other MCP clients without Entra DCR support can use it.

Authoritative design spec:
[`docs/specs/2026-06-09-ado-mcp-bridge-design.md`](docs/specs/2026-06-09-ado-mcp-bridge-design.md).

Decisions already locked (see spec §10): .NET 10, GitHub Actions, one
shared Entra app with certificate auth, Azure Container Apps, built-in
`ILogger`, 100% test coverage with annotated exclusions, CI-publishes-
release distribution model. Do not re-litigate these without a strong
new signal.

## Working agreements

- TDD: red → green → refactor when implementation begins.
- No EF in-memory provider in tests; integration tests use real SQL.
- Never log tokens, codes, or PKCE verifiers (Roslyn analyzer will
  enforce once implementation lands).
- Prefer built-in MS stack libraries over third-party where capability
  is equivalent (e.g. `ILogger` over Serilog).
