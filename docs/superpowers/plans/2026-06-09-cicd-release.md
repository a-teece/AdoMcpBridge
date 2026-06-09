# CI/CD & Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the GitHub Actions CI/CD pipelines, the `deploy.ps1` cross-platform installer, and the SemVer release plumbing so every tagged release ships a signed GHCR image, an SBOM, a versioned Bicep bundle, and a release-notes-driven GitHub Release that adopters can deploy with one command.

**Architecture:** Four workflows live under `.github/workflows/`: `ci.yml` (PR + push gate with 100% coverage), `release.yml` (tag-driven container build / cosign sign / SBOM / asset packaging / release notes), `reusable-cd.yml` (a `workflow_call` consumed by adopters to deploy a chosen tag via OIDC federated identity), and `pr-template-check.yml` (blocks alert changes that lack runbook entries). Adopters use `deploy.ps1` which is a thin wrapper over `az deployment group create` with a `cosign verify` pre-flight. SemVer bumps are automated by `release-please` from Conventional Commit messages.

**Tech Stack:** GitHub Actions, GHCR, Sigstore cosign (keyless OIDC), anchore/sbom-action (SPDX JSON), Codecov, Coverlet (MSBuild integration), release-please-action, Azure CLI / Bicep, PowerShell 7+, Pester 5.x.

---

## File Structure

| Path | Responsibility |
| --- | --- |
| `.github/workflows/ci.yml` | Build + format-verify + test with 100% coverage gate; uploads Codecov. |
| `.github/workflows/release.yml` | Tag-driven container build, cosign sign, SBOM, Bicep zip, GH Release. |
| `.github/workflows/reusable-cd.yml` | `workflow_call` deploy entrypoint for adopters. |
| `.github/workflows/pr-template-check.yml` | Enforces "no new alert without runbook entry". |
| `deploy.ps1` | Cross-platform PowerShell 7+ deploy wrapper with cosign verify. |
| `tests/deploy/deploy.Tests.ps1` | Pester 5 tests for `deploy.ps1`. |
| `CHANGELOG.md` | Keep-a-Changelog seed; release-please appends. |
| `SECURITY.md` | Private disclosure address. |
| `compatibility.md` | Bridge version → MS Remote MCP API generation matrix. |
| `release-please-config.json` | release-please package config. |
| `.release-please-manifest.json` | release-please version manifest. |
| `.github/release-please/.gitkeep` | Hold-open for any future release-please overlays. |
| `codecov.yml` | Codecov target (100% project, informational patch). |

---

### Task 1: Repo-root meta files (CHANGELOG, SECURITY, compatibility)

**Files:**
- Create: `CHANGELOG.md`
- Create: `SECURITY.md`
- Create: `compatibility.md`

- [ ] **Step 1: Create `CHANGELOG.md`**

```markdown
# Changelog

All notable changes to **ADO MCP Bridge** are documented here.
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
and the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
`release-please` maintains entries below this line from Conventional
Commit messages — do not hand-edit released sections.

## [Unreleased]

### Added
- Initial scaffolding.
```

- [ ] **Step 2: Create `SECURITY.md`**

```markdown
# Security Policy

## Supported Versions

Only the latest minor release line of `ado-mcp-bridge` receives security
fixes. See [`compatibility.md`](./compatibility.md) for the
bridge-version → Microsoft Remote MCP API mapping.

## Reporting a Vulnerability

**Do not open a public GitHub issue for security reports.**

Email: `security@enate.net` (PGP key fingerprint published at
`https://github.com/<owner>/AdoMcpBridge/blob/main/.github/security-pgp.asc`).

We acknowledge within **2 business days** and aim to ship a fix or
mitigation within **30 days** for high-severity issues. Coordinated
disclosure preferred; credit given in `CHANGELOG.md` unless you opt out.
```

- [ ] **Step 3: Create `compatibility.md`**

```markdown
# Compatibility Matrix

| Bridge version | Microsoft Remote MCP API generation | Notes |
| --- | --- | --- |
| v0.1.0 | API generation 1 (2026-Q2 baseline) | First public release. Tracks `https://mcp.dev.azure.com/{org}` as documented at https://learn.microsoft.com/en-us/azure/devops/mcp-server/remote-mcp-server. |

When MS ships a breaking change to the Remote MCP server we bump the
bridge **minor** version and add a new row. Patch releases never change
the API generation.
```

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md SECURITY.md compatibility.md
git commit -m "docs: seed CHANGELOG, SECURITY, and compatibility matrix"
```

---

### Task 2: release-please configuration

**Files:**
- Create: `release-please-config.json`
- Create: `.release-please-manifest.json`

We use `release-please` (over a hand-rolled `gh release create`) because
the spec mandates SemVer + `CHANGELOG.md` and the bridge will have
multiple shipping artefacts (container, Bicep zip, deploy.ps1, SBOM)
that need a single coordinated tag. `release-please` derives the bump
from Conventional Commits — the policy our `CLAUDE.md` already enforces
— so authors get correct SemVer for free, and `release.yml` is triggered
by the tag it produces. A bare `gh release create` would re-implement
all of this.

- [ ] **Step 1: Create `release-please-config.json`**

```json
{
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json",
  "release-type": "simple",
  "include-v-in-tag": true,
  "include-component-in-tag": false,
  "bump-minor-pre-major": true,
  "bump-patch-for-minor-pre-major": true,
  "packages": {
    ".": {
      "package-name": "ado-mcp-bridge",
      "changelog-path": "CHANGELOG.md",
      "extra-files": ["compatibility.md"]
    }
  },
  "plugins": []
}
```

- [ ] **Step 2: Create `.release-please-manifest.json`**

```json
{
  ".": "0.0.0"
}
```

- [ ] **Step 3: Commit**

```bash
git add release-please-config.json .release-please-manifest.json
git commit -m "chore: add release-please config for SemVer automation"
```

---

### Task 3: Codecov target file

**Files:**
- Create: `codecov.yml`

- [ ] **Step 1: Create `codecov.yml`**

```yaml
coverage:
  status:
    project:
      default:
        target: 100%
        threshold: 0%
        if_ci_failed: error
    patch:
      default:
        informational: true

comment:
  layout: "reach, diff, flags, files"
  behavior: default
  require_changes: false

ignore:
  - "tests/**"
  - "**/Migrations/**"
```

- [ ] **Step 2: Validate the file is parseable**

Run: `python -c "import yaml,sys; yaml.safe_load(open('codecov.yml'))"`
Expected: exits 0, no output.

- [ ] **Step 3: Commit**

```bash
git add codecov.yml
git commit -m "chore: add codecov config enforcing 100% project coverage"
```

---

### Task 4: CI workflow — build, format, test, coverage

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Write `.github/workflows/ci.yml`**

```yaml
name: ci

on:
  pull_request:
    branches: [main]
  push:
    branches: [main]

permissions:
  contents: read

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

jobs:
  build-test:
    name: Build, format, test (100% coverage)
    runs-on: ubuntu-24.04
    timeout-minutes: 25
    services:
      sql:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: "Y"
          MSSQL_SA_PASSWORD: "Y0ur_strong!Passw0rd"
        ports:
          - 1433:1433
        options: >-
          --health-cmd "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P Y0ur_strong!Passw0rd -Q 'SELECT 1'"
          --health-interval 10s
          --health-timeout 5s
          --health-retries 20
    env:
      DOTNET_NOLOGO: "true"
      DOTNET_CLI_TELEMETRY_OPTOUT: "true"
      ADOMCP_TEST_SQL_CONNECTION: "Server=localhost,1433;Database=master;User Id=sa;Password=Y0ur_strong!Passw0rd;TrustServerCertificate=True;"
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Cache NuGet packages
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: nuget-${{ runner.os }}-${{ hashFiles('**/*.csproj', '**/Directory.Packages.props') }}
          restore-keys: nuget-${{ runner.os }}-

      - name: Restore
        run: dotnet restore AdoMcpBridge.sln

      - name: Verify formatting
        run: dotnet format AdoMcpBridge.sln --verify-no-changes --severity error

      - name: Build (Release)
        run: dotnet build AdoMcpBridge.sln --configuration Release --no-restore

      - name: Test with coverage gate
        run: >-
          dotnet test AdoMcpBridge.sln
          --configuration Release
          --no-build
          --logger "trx;LogFileName=test-results.trx"
          /p:CollectCoverage=true
          /p:Threshold=100
          /p:ThresholdType=line,branch,method
          /p:ThresholdStat=total
          /p:CoverletOutputFormat=cobertura
          /p:CoverletOutput=./TestResults/coverage.cobertura.xml
          /p:ExcludeByAttribute=Obsolete%2cGeneratedCodeAttribute%2cCompilerGeneratedAttribute

      - name: Upload coverage to Codecov
        uses: codecov/codecov-action@v4
        with:
          files: "**/TestResults/coverage.cobertura.xml"
          fail_ci_if_error: true
          flags: unittests
          token: ${{ secrets.CODECOV_TOKEN }}

      - name: Upload test results artifact
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: "**/TestResults/*.trx"
          if-no-files-found: warn
```

- [ ] **Step 2: Smoke the workflow locally with `act`**

Run: `act pull_request -W .github/workflows/ci.yml -j build-test --container-architecture linux/amd64 -P ubuntu-24.04=catthehacker/ubuntu:act-24.04`
Expected: Either passes, or fails at the .NET install / test stage — `act` cannot run the SQL service reliably; failures beyond the YAML-parse stage are acceptable here. The YAML must parse without "invalid workflow" errors.

- [ ] **Step 3: Validate workflow syntax with `actionlint`**

Run: `docker run --rm -v "$PWD":/repo --workdir /repo rhysd/actionlint:latest -color`
Expected: exits 0 with no findings.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add build/test workflow with 100% coverage gate"
```

---

### Task 5: Release workflow — image, cosign, SBOM, assets

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Write `.github/workflows/release.yml`**

```yaml
name: release

on:
  push:
    tags: ["v*.*.*"]

permissions:
  contents: write
  packages: write
  id-token: write   # required for cosign keyless + GHCR OIDC

concurrency:
  group: release-${{ github.ref }}
  cancel-in-progress: false

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository_owner }}/adomcpbridge

jobs:
  build-and-release:
    name: Build, sign, package, release
    runs-on: ubuntu-24.04
    timeout-minutes: 45
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Extract version
        id: ver
        run: |
          TAG="${GITHUB_REF_NAME}"
          echo "tag=${TAG}" >> "$GITHUB_OUTPUT"
          echo "version=${TAG#v}" >> "$GITHUB_OUTPUT"

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push image
        id: build
        uses: docker/build-push-action@v6
        with:
          context: .
          file: src/AdoMcpBridge.Api/Dockerfile
          push: true
          provenance: true
          sbom: false
          tags: |
            ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ steps.ver.outputs.tag }}
            ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:latest
          labels: |
            org.opencontainers.image.source=https://github.com/${{ github.repository }}
            org.opencontainers.image.version=${{ steps.ver.outputs.version }}
            org.opencontainers.image.revision=${{ github.sha }}

      - name: Install cosign
        uses: sigstore/cosign-installer@v3
        with:
          cosign-release: v2.4.1

      - name: Sign image (keyless)
        env:
          COSIGN_EXPERIMENTAL: "1"
        run: |
          cosign sign --yes \
            "${REGISTRY}/${IMAGE_NAME}@${{ steps.build.outputs.digest }}"

      - name: Generate SBOM (SPDX JSON)
        uses: anchore/sbom-action@v0
        with:
          image: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ steps.ver.outputs.tag }}
          format: spdx-json
          output-file: ./adomcpbridge-${{ steps.ver.outputs.tag }}.sbom.spdx.json
          upload-artifact: false
          upload-release-assets: false

      - name: Attest SBOM to image
        env:
          COSIGN_EXPERIMENTAL: "1"
        run: |
          cosign attest --yes \
            --predicate "./adomcpbridge-${{ steps.ver.outputs.tag }}.sbom.spdx.json" \
            --type spdxjson \
            "${REGISTRY}/${IMAGE_NAME}@${{ steps.build.outputs.digest }}"

      - name: Package Bicep templates
        run: |
          mkdir -p dist
          (cd infra && zip -r "../dist/bicep-${{ steps.ver.outputs.tag }}.zip" .)

      - name: Stage deploy.ps1
        run: cp deploy.ps1 "dist/deploy-${{ steps.ver.outputs.tag }}.ps1"

      - name: Stage SBOM
        run: cp "./adomcpbridge-${{ steps.ver.outputs.tag }}.sbom.spdx.json" dist/

      - name: Extract release notes from CHANGELOG
        id: notes
        run: |
          python - <<'PY' >> "$GITHUB_OUTPUT"
          import os, re, pathlib
          tag = os.environ["TAG"]
          version = tag.lstrip("v")
          text = pathlib.Path("CHANGELOG.md").read_text()
          # Section is "## [<version>]" up to the next "## [" header.
          pattern = rf"## \[{re.escape(version)}\][^\n]*\n(.*?)(?=\n## \[|\Z)"
          m = re.search(pattern, text, re.DOTALL)
          body = m.group(1).strip() if m else f"Release {tag}."
          print("body<<EOF")
          print(body)
          print("EOF")
          PY
        env:
          TAG: ${{ steps.ver.outputs.tag }}

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          tag_name: ${{ steps.ver.outputs.tag }}
          name: ${{ steps.ver.outputs.tag }}
          body: ${{ steps.notes.outputs.body }}
          fail_on_unmatched_files: true
          files: |
            dist/bicep-${{ steps.ver.outputs.tag }}.zip
            dist/deploy-${{ steps.ver.outputs.tag }}.ps1
            dist/adomcpbridge-${{ steps.ver.outputs.tag }}.sbom.spdx.json
```

- [ ] **Step 2: Lint the workflow with `actionlint`**

Run: `docker run --rm -v "$PWD":/repo --workdir /repo rhysd/actionlint:latest -color .github/workflows/release.yml`
Expected: exits 0.

- [ ] **Step 3: Dry-run trigger documentation**

Manual verification (we cannot fully run release locally because it pushes to GHCR):

```bash
# After merging the PR that introduces release.yml and a Dockerfile:
gh workflow run release.yml --ref main
# Or, to actually trigger it on a tag in a fork:
git tag v0.0.1-rc1 && git push origin v0.0.1-rc1
gh run watch
```

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: add tag-driven release workflow with cosign and SBOM"
```

---

### Task 6: Reusable CD workflow — OIDC deploy for adopters

**Files:**
- Create: `.github/workflows/reusable-cd.yml`

- [ ] **Step 1: Write `.github/workflows/reusable-cd.yml`**

```yaml
name: reusable-cd

on:
  workflow_call:
    inputs:
      tag:
        description: "Bridge version tag to deploy (e.g. v0.1.0)."
        required: true
        type: string
      environment:
        description: "Target environment name (dev | prod)."
        required: true
        type: string
      subscription_id:
        description: "Azure subscription GUID."
        required: true
        type: string
      tenant_id:
        description: "Entra tenant GUID."
        required: true
        type: string
      resource_group:
        description: "Target resource group (e.g. rg-adomcp-prod)."
        required: true
        type: string
    secrets:
      azure_client_id:
        description: "Client ID of the federated identity credential for OIDC login."
        required: true

permissions:
  id-token: write
  contents: read

jobs:
  deploy:
    name: Deploy ${{ inputs.tag }} to ${{ inputs.environment }}
    runs-on: ubuntu-24.04
    environment: ${{ inputs.environment }}
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v4
        with:
          ref: ${{ inputs.tag }}

      - name: Azure login (OIDC federated)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.azure_client_id }}
          tenant-id: ${{ inputs.tenant_id }}
          subscription-id: ${{ inputs.subscription_id }}

      - name: Install cosign
        uses: sigstore/cosign-installer@v3
        with:
          cosign-release: v2.4.1

      - name: Install PowerShell 7
        shell: bash
        run: |
          if ! command -v pwsh >/dev/null; then
            sudo apt-get update
            sudo apt-get install -y wget apt-transport-https software-properties-common
            wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
            sudo dpkg -i packages-microsoft-prod.deb
            sudo apt-get update
            sudo apt-get install -y powershell
          fi
          pwsh -v

      - name: Run deploy.ps1
        shell: pwsh
        run: |
          ./deploy.ps1 `
            -Env "${{ inputs.environment }}" `
            -Tag "${{ inputs.tag }}" `
            -SubscriptionId "${{ inputs.subscription_id }}" `
            -ResourceGroup "${{ inputs.resource_group }}"
```

- [ ] **Step 2: Lint with `actionlint`**

Run: `docker run --rm -v "$PWD":/repo --workdir /repo rhysd/actionlint:latest -color .github/workflows/reusable-cd.yml`
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/reusable-cd.yml
git commit -m "ci: add reusable CD workflow with OIDC federated auth"
```

---

### Task 7: PR template alert/runbook gate

**Files:**
- Create: `.github/workflows/pr-template-check.yml`

- [ ] **Step 1: Write `.github/workflows/pr-template-check.yml`**

```yaml
name: pr-template-check

on:
  pull_request:
    branches: [main]
    paths:
      - "infra/modules/observability.bicep"
      - "infra/alerts/**"
      - "docs/runbook.md"

permissions:
  contents: read
  pull-requests: read

jobs:
  alert-runbook-gate:
    name: New alert requires runbook entry
    runs-on: ubuntu-24.04
    timeout-minutes: 5
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Detect added alerts vs. added runbook entries
        env:
          BASE_SHA: ${{ github.event.pull_request.base.sha }}
          HEAD_SHA: ${{ github.event.pull_request.head.sha }}
        run: |
          set -euo pipefail
          # 1. Added alert lines in observability.bicep or infra/alerts/
          ADDED_ALERTS=$(git diff "$BASE_SHA".."$HEAD_SHA" -- \
              infra/modules/observability.bicep infra/alerts/ \
            | grep -E "^\+" | grep -v "^\+\+\+" \
            | grep -Eci "Microsoft\.Insights/(metricAlerts|scheduledQueryRules)|resource[[:space:]]+\w+[[:space:]]+'Microsoft\.Insights/" \
            || true)

          # 2. Added runbook entries — any new "### " heading in docs/runbook.md
          ADDED_RUNBOOK=$(git diff "$BASE_SHA".."$HEAD_SHA" -- docs/runbook.md \
            | grep -Ec "^\+### " \
            || true)

          echo "Added alert lines: $ADDED_ALERTS"
          echo "Added runbook headings: $ADDED_RUNBOOK"

          if [ "$ADDED_ALERTS" -gt 0 ] && [ "$ADDED_RUNBOOK" -lt 1 ]; then
            echo "::error::PR introduces alert(s) under infra/modules/observability.bicep or infra/alerts/ but adds no '### ' entry to docs/runbook.md."
            exit 1
          fi
```

- [ ] **Step 2: Lint with `actionlint`**

Run: `docker run --rm -v "$PWD":/repo --workdir /repo rhysd/actionlint:latest -color .github/workflows/pr-template-check.yml`
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/pr-template-check.yml
git commit -m "ci: enforce no new alert without runbook entry"
```

---

### Task 8: release-please scheduled workflow

**Files:**
- Create: `.github/workflows/release-please.yml`

- [ ] **Step 1: Write `.github/workflows/release-please.yml`**

```yaml
name: release-please

on:
  push:
    branches: [main]

permissions:
  contents: write
  pull-requests: write

jobs:
  release-please:
    name: release-please PR / tag
    runs-on: ubuntu-24.04
    steps:
      - uses: googleapis/release-please-action@v4
        with:
          config-file: release-please-config.json
          manifest-file: .release-please-manifest.json
          token: ${{ secrets.GITHUB_TOKEN }}
```

- [ ] **Step 2: Lint**

Run: `docker run --rm -v "$PWD":/repo --workdir /repo rhysd/actionlint:latest -color .github/workflows/release-please.yml`
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release-please.yml
git commit -m "ci: run release-please on main to auto-cut release PRs"
```

---

### Task 9: `deploy.ps1` skeleton + parameter validation

**Files:**
- Create: `deploy.ps1`

- [ ] **Step 1: Write `deploy.ps1`**

```powershell
#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploys the ADO MCP Bridge to an Azure subscription.

.DESCRIPTION
    Wraps `az deployment group create` against infra/main.bicep with the
    per-environment .bicepparam file. Before deploying, verifies that
    the requested GHCR image+tag exists and carries a valid cosign
    signature.

.PARAMETER Env
    Target environment: dev or prod.

.PARAMETER Tag
    Bridge version tag to deploy (e.g. v0.1.0). Must match GHCR image tag.

.PARAMETER SubscriptionId
    Azure subscription GUID.

.PARAMETER ResourceGroup
    Target resource group (e.g. rg-adomcp-prod).

.PARAMETER ImageOwner
    GHCR namespace; defaults to the upstream "AdoMcpBridge" owner.

.EXAMPLE
    ./deploy.ps1 -Env prod -Tag v0.1.0 `
        -SubscriptionId 11111111-1111-1111-1111-111111111111 `
        -ResourceGroup rg-adomcp-prod
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('dev', 'prod')] [string] $Env,
    [Parameter(Mandatory)] [ValidatePattern('^v\d+\.\d+\.\d+(-[A-Za-z0-9\.\-]+)?$')] [string] $Tag,
    [Parameter(Mandatory)] [ValidatePattern('^[0-9a-fA-F-]{36}$')] [string] $SubscriptionId,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $ResourceGroup,
    [Parameter()] [string] $ImageOwner = 'enateltd'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Command {
    param([Parameter(Mandatory)][string] $Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' not found on PATH."
    }
}

function Invoke-PreflightVerify {
    param(
        [Parameter(Mandatory)][string] $ImageRef,
        [Parameter(Mandatory)][string] $RepoUrl
    )

    Write-Host "Verifying cosign signature on $ImageRef ..." -ForegroundColor Cyan
    & cosign verify `
        --certificate-identity-regexp "^https://github\.com/$RepoUrl/\.github/workflows/release\.yml@refs/tags/" `
        --certificate-oidc-issuer 'https://token.actions.githubusercontent.com' `
        $ImageRef | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "cosign verify failed for $ImageRef."
    }
}

function Invoke-Deploy {
    Assert-Command 'az'
    Assert-Command 'cosign'

    $imageRef = "ghcr.io/$ImageOwner/adomcpbridge:$Tag"
    $repoUrl  = "$ImageOwner/AdoMcpBridge"

    Invoke-PreflightVerify -ImageRef $imageRef -RepoUrl $repoUrl

    Write-Host "Setting subscription $SubscriptionId ..." -ForegroundColor Cyan
    & az account set --subscription $SubscriptionId | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "az account set failed." }

    $paramFile = Join-Path $PSScriptRoot "infra/main.$Env.bicepparam"
    if (-not (Test-Path $paramFile)) {
        throw "Parameter file not found: $paramFile"
    }

    Write-Host "Deploying $imageRef to $ResourceGroup ..." -ForegroundColor Cyan
    & az deployment group create `
        --resource-group $ResourceGroup `
        --template-file (Join-Path $PSScriptRoot 'infra/main.bicep') `
        --parameters $paramFile `
        --parameters "containerImage=$imageRef" `
        --name "adomcp-$Tag-$(Get-Date -Format yyyyMMddHHmmss)"

    if ($LASTEXITCODE -ne 0) { throw "az deployment group create failed." }

    Write-Host "Deployment complete." -ForegroundColor Green
}

# Skip execution when dot-sourced (so Pester can test the functions).
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-Deploy
}
```

- [ ] **Step 2: Lint with PSScriptAnalyzer**

Run: `pwsh -NoProfile -Command "Install-Module PSScriptAnalyzer -Force -Scope CurrentUser -SkipPublisherCheck; Invoke-ScriptAnalyzer -Path ./deploy.ps1 -Severity Warning,Error -EnableExit"`
Expected: exits 0 with no findings.

- [ ] **Step 3: Commit**

```bash
git add deploy.ps1
git commit -m "feat: add cross-platform deploy.ps1 with cosign preflight"
```

---

### Task 10: Pester tests for `deploy.ps1`

**Files:**
- Create: `tests/deploy/deploy.Tests.ps1`

- [ ] **Step 1: Write the failing test file**

```powershell
#Requires -Version 7.0
#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.5.0' }

BeforeAll {
    $script:DeployScript = Join-Path $PSScriptRoot '../../deploy.ps1'
    if (-not (Test-Path $script:DeployScript)) {
        throw "deploy.ps1 not found at $script:DeployScript"
    }
    . $script:DeployScript
}

Describe 'deploy.ps1 parameter validation' {
    It 'rejects an invalid environment' {
        { & $script:DeployScript -Env 'staging' -Tag 'v0.1.0' `
            -SubscriptionId '11111111-1111-1111-1111-111111111111' `
            -ResourceGroup 'rg-adomcp-staging' } |
            Should -Throw -ErrorId 'ParameterArgumentValidationError*'
    }

    It 'rejects a non-SemVer tag' {
        { & $script:DeployScript -Env 'dev' -Tag '0.1' `
            -SubscriptionId '11111111-1111-1111-1111-111111111111' `
            -ResourceGroup 'rg-adomcp-dev' } |
            Should -Throw -ErrorId 'ParameterArgumentValidationError*'
    }

    It 'rejects a non-GUID subscription' {
        { & $script:DeployScript -Env 'dev' -Tag 'v0.1.0' `
            -SubscriptionId 'not-a-guid' `
            -ResourceGroup 'rg-adomcp-dev' } |
            Should -Throw -ErrorId 'ParameterArgumentValidationError*'
    }
}

Describe 'Assert-Command' {
    It 'throws when the named command is missing' {
        { Assert-Command -Name 'definitely-not-a-real-command-xyz' } |
            Should -Throw "*not found on PATH*"
    }

    It 'returns silently when the named command exists' {
        { Assert-Command -Name 'pwsh' } | Should -Not -Throw
    }
}

Describe 'Invoke-PreflightVerify' {
    BeforeAll {
        function global:cosign { $global:LASTEXITCODE = 0 }
    }

    AfterAll {
        Remove-Item Function:\cosign -ErrorAction SilentlyContinue
    }

    It 'throws when cosign exits non-zero' {
        function global:cosign { $global:LASTEXITCODE = 7 }
        { Invoke-PreflightVerify -ImageRef 'ghcr.io/x/y:v0.0.1' -RepoUrl 'x/y' } |
            Should -Throw "*cosign verify failed*"
    }

    It 'returns silently when cosign exits zero' {
        function global:cosign { $global:LASTEXITCODE = 0 }
        { Invoke-PreflightVerify -ImageRef 'ghcr.io/x/y:v0.0.1' -RepoUrl 'x/y' } |
            Should -Not -Throw
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail before fix-ups**

Run: `pwsh -NoProfile -Command "Install-Module Pester -MinimumVersion 5.5.0 -Force -Scope CurrentUser -SkipPublisherCheck; Invoke-Pester ./tests/deploy/deploy.Tests.ps1 -CI"`
Expected: All `parameter validation` and `Assert-Command` tests pass immediately (deploy.ps1 already implements them). If any fail, fix `deploy.ps1` until green.

- [ ] **Step 3: Run again to verify green**

Run: `pwsh -NoProfile -Command "Invoke-Pester ./tests/deploy/deploy.Tests.ps1 -CI"`
Expected: 0 failed.

- [ ] **Step 4: Commit**

```bash
git add tests/deploy/deploy.Tests.ps1
git commit -m "test: add Pester tests for deploy.ps1"
```

---

### Task 11: Wire Pester run into CI

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add a `deploy-script-tests` job to `ci.yml`**

Append the following job alongside `build-test:` (same indentation level under `jobs:`):

```yaml
  deploy-script-tests:
    name: Pester tests for deploy.ps1
    runs-on: ubuntu-24.04
    timeout-minutes: 10
    steps:
      - uses: actions/checkout@v4

      - name: Install Pester and PSScriptAnalyzer
        shell: pwsh
        run: |
          Install-Module Pester -MinimumVersion 5.5.0 -Force -Scope CurrentUser -SkipPublisherCheck
          Install-Module PSScriptAnalyzer -Force -Scope CurrentUser -SkipPublisherCheck

      - name: Lint deploy.ps1
        shell: pwsh
        run: Invoke-ScriptAnalyzer -Path ./deploy.ps1 -Severity Warning,Error -EnableExit

      - name: Run Pester
        shell: pwsh
        run: |
          $cfg = New-PesterConfiguration
          $cfg.Run.Path = './tests/deploy/deploy.Tests.ps1'
          $cfg.Run.Exit = $true
          $cfg.Output.Verbosity = 'Detailed'
          $cfg.TestResult.Enabled = $true
          $cfg.TestResult.OutputPath = './TestResults/deploy.pester.xml'
          Invoke-Pester -Configuration $cfg

      - name: Upload Pester results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: deploy-pester-results
          path: TestResults/deploy.pester.xml
          if-no-files-found: warn
```

- [ ] **Step 2: Re-lint the modified workflow**

Run: `docker run --rm -v "$PWD":/repo --workdir /repo rhysd/actionlint:latest -color .github/workflows/ci.yml`
Expected: exits 0.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: run Pester deploy.ps1 tests in CI"
```

---

### Task 12: Pull request template

**Files:**
- Create: `.github/pull_request_template.md`

- [ ] **Step 1: Write the template**

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add .github/pull_request_template.md
git commit -m "docs: add PR template referencing alert/runbook gate"
```

---

### Task 13: Adopter consumer-workflow example

**Files:**
- Create: `docs/adopters/example-consumer.yml`

This file is documentation-only — it shows adopters how to call
`reusable-cd.yml` from their own private repo.

- [ ] **Step 1: Write the example**

```yaml
# Drop this into <adopter-repo>/.github/workflows/deploy-adomcp.yml and
# adjust inputs. Requires an Entra federated identity credential whose
# subject matches `repo:<adopter-owner>/<adopter-repo>:environment:prod`.
name: deploy-adomcp

on:
  workflow_dispatch:
    inputs:
      tag:
        description: "Bridge tag to deploy (e.g. v0.1.0)"
        required: true
        type: string

permissions:
  id-token: write
  contents: read

jobs:
  prod:
    uses: enateltd/AdoMcpBridge/.github/workflows/reusable-cd.yml@v0.1.0
    with:
      tag: ${{ inputs.tag }}
      environment: prod
      subscription_id: 00000000-0000-0000-0000-000000000000
      tenant_id:       00000000-0000-0000-0000-000000000000
      resource_group:  rg-adomcp-prod
    secrets:
      azure_client_id: ${{ secrets.AZURE_CLIENT_ID }}
```

- [ ] **Step 2: Commit**

```bash
git add docs/adopters/example-consumer.yml
git commit -m "docs: add adopter example for reusable-cd workflow"
```

---

### Task 14: End-to-end smoke (manual verification)

There is no automated harness for "tag → release → adopter deploy" — it
crosses GHCR, cosign's public Rekor, and a real Azure subscription. The
plan author / operator runs this once after Task 13 lands on `main`.

- [ ] **Step 1: Cut a pre-release tag**

```bash
git checkout main && git pull
git tag v0.0.1-rc1
git push origin v0.0.1-rc1
gh run watch
```

Expected: `release.yml` produces a GitHub Release with three assets
(`bicep-v0.0.1-rc1.zip`, `deploy-v0.0.1-rc1.ps1`,
`adomcpbridge-v0.0.1-rc1.sbom.spdx.json`) and an image
`ghcr.io/<owner>/adomcpbridge:v0.0.1-rc1`.

- [ ] **Step 2: Verify the signature out-of-band**

```bash
cosign verify \
  --certificate-identity-regexp "^https://github\.com/<owner>/AdoMcpBridge/\.github/workflows/release\.yml@refs/tags/" \
  --certificate-oidc-issuer 'https://token.actions.githubusercontent.com' \
  ghcr.io/<owner>/adomcpbridge:v0.0.1-rc1
```

Expected: "Verified OK" and at least one Rekor entry printed.

- [ ] **Step 3: Run `deploy.ps1` against the dev subscription**

```bash
pwsh ./deploy.ps1 -Env dev -Tag v0.0.1-rc1 \
    -SubscriptionId <dev-sub-guid> \
    -ResourceGroup rg-adomcp-dev
```

Expected: `cosign verify` succeeds, `az deployment group create`
succeeds, the Container App `ca-adomcp-dev` ends in `Succeeded`
provisioning state.

- [ ] **Step 4: Delete the pre-release**

```bash
gh release delete v0.0.1-rc1 --cleanup-tag --yes
```

(No commit — this task is a verification gate, not a code change.)

---

## Self-Review Notes

**Spec coverage (§4 + §7 + §10 of the design spec, plus the user's scope list):**

- 100% coverage gate via Coverlet `Threshold=100 ThresholdType=line,branch,method` — Task 4.
- `dotnet format --verify-no-changes` — Task 4.
- Codecov upload — Task 4 + Task 3 (`codecov.yml`).
- Tag-driven container build to `ghcr.io/<owner>/adomcpbridge:vX.Y.Z` and `:latest` — Task 5.
- Cosign keyless sign via `sigstore/cosign-installer` — Task 5.
- SBOM via `anchore/sbom-action` and attested to the image — Task 5.
- Bicep zip + `deploy.ps1` + SBOM attached to GH Release — Task 5.
- Release notes from `CHANGELOG.md` — Task 5 (regex extracts the `[<version>]` section).
- Reusable CD workflow with `tag` / `environment` / `subscription_id` / `tenant_id` / `resource_group` inputs and OIDC `azure/login@v2` — Task 6.
- PR alert-without-runbook gate — Task 7 (regex against diff of `infra/modules/observability.bicep` + `infra/alerts/` vs. `docs/runbook.md`).
- `deploy.ps1` with `-Env`, `-Tag`, `-SubscriptionId`, `-ResourceGroup` and `cosign verify` pre-flight — Task 9.
- Pester tests for `deploy.ps1` at `tests/deploy/deploy.Tests.ps1` — Tasks 10 + 11.
- `CHANGELOG.md`, `SECURITY.md` (private disclosure), `compatibility.md` with v0.1.0 → "API generation 1 (2026-Q2 baseline)" — Task 1.
- `release-please` config + manifest with justification over `gh release create` — Task 2.
- `act` local smoke command — Task 4 Step 2.
- Manual `gh workflow run` / `gh run watch` verification commands — Tasks 5 and 14.

**Placeholder scan:** No "TBD" / "implement later" / "similar to" tokens. Every workflow file step includes the full YAML; every PowerShell step includes full code; every Pester test step includes full test code.

**Type / naming consistency:**

- Image reference shape `ghcr.io/<owner>/adomcpbridge:<tag>` is identical across `release.yml`, `deploy.ps1`, `reusable-cd.yml`, and the example consumer.
- Environment names `dev` / `prod` match Bicep param file convention from `_shared-contracts.md` (`infra/main.{env}.bicepparam`).
- Resource-group names `rg-adomcp-{env}` and Container App `ca-adomcp-{env}` match `_shared-contracts.md` §"Naming for deployed Azure resources".
- SemVer regex (`^v\d+\.\d+\.\d+(-[A-Za-z0-9\.\-]+)?$`) tolerates `v0.0.1-rc1` used in Task 14.
- Cosign keyless cert-identity regex in `deploy.ps1` matches the workflow path `.github/workflows/release.yml` defined in Task 5.

**Cross-plan boundaries (intentionally out of scope here):**

- `infra/main.bicep`, `infra/main.dev.bicepparam`, `infra/main.prod.bicepparam`, `infra/modules/observability.bicep`, `infra/alerts/`, and `docs/runbook.md` are produced by `2026-06-09-infra-bicep.md` and `2026-06-09-observability-runbook.md`. This plan only references them.
- `src/AdoMcpBridge.Api/Dockerfile` is produced by the foundation / API host plan; Task 5 references it but does not create it.
- Nightly smoke tests + the auto-issue-on-failure handler are owned by `2026-06-09-smoke-connector.md`.
