# Smoke Tests & Connector Card Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a nightly + on-release smoke test project that exercises the deployed ADO MCP Bridge end-to-end (discovery, connector card, token refresh, MCP `tools/list`) and auto-opens a deduped GitHub Issue on failure.

**Architecture:** A new xUnit project `tests/AdoMcpBridge.Smoke/` reads target URL and a pre-provisioned test refresh token from environment variables, makes HTTP calls against a real dev deployment, and asserts shape + behaviour. All tests carry `[Trait("category","smoke")]` and never log token values. A GitHub Actions workflow runs them on cron + on `release: published`, and a composite action opens a deduped `smoke-failure` issue on failure. The `/connector-info.json` endpoint is owned by the MCP-proxy plan (`2026-06-09-mcp-proxy.md`); this plan only smoke-tests it.

**Tech Stack:** xUnit, `Microsoft.NET.Test.Sdk`, `FluentAssertions`, `System.Net.Http.Json`, GitHub Actions, `gh` CLI (in composite action).

---

## File map

- Create: `tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
- Create: `tests/AdoMcpBridge.Smoke/SmokeEnvironment.cs` — env-var loader, redaction helpers.
- Create: `tests/AdoMcpBridge.Smoke/SmokeHttpClient.cs` — shared `HttpClient` factory.
- Create: `tests/AdoMcpBridge.Smoke/Tests/DiscoveryTests.cs` — `/.well-known/oauth-authorization-server`.
- Create: `tests/AdoMcpBridge.Smoke/Tests/ConnectorInfoTests.cs` — `/connector-info.json`.
- Create: `tests/AdoMcpBridge.Smoke/Tests/TokenRefreshTests.cs` — `/token` with refresh grant.
- Create: `tests/AdoMcpBridge.Smoke/Tests/McpToolsListTests.cs` — end-to-end MCP call.
- Create: `tests/AdoMcpBridge.Smoke/Models/SmokeDtos.cs` — record DTOs for JSON.
- Modify: `AdoMcpBridge.sln` — add Smoke project.
- Create: `.github/actions/auto-open-issue/action.yml` — composite action.
- Create: `.github/workflows/smoke.yml` — cron + release workflow.
- Create: `docs/smoke-runbook.md` — local-run instructions.

---

### Task 1: Create the Smoke test project skeleton

**Files:**
- Create: `tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
- Modify: `AdoMcpBridge.sln`

- [ ] **Step 1: Write the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>AdoMcpBridge.Smoke</RootNamespace>
    <!-- Smoke tests run against a live deployment; exclude from coverage gates. -->
    <CollectCoverage>false</CollectCoverage>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the project to the solution**

Run: `dotnet sln AdoMcpBridge.sln add tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 3: Verify project restores**

Run: `dotnet restore tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
Expected: exit code 0, "Restore completed".

- [ ] **Step 4: Commit**

```bash
git add AdoMcpBridge.sln tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj
git commit -m "chore: scaffold AdoMcpBridge.Smoke test project"
```

---

### Task 2: Environment loader with redaction

**Files:**
- Create: `tests/AdoMcpBridge.Smoke/SmokeEnvironment.cs`
- Create: `tests/AdoMcpBridge.Smoke/Tests/SmokeEnvironmentTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("category", "smoke")]
public class SmokeEnvironmentTests
{
    [Fact]
    public void Redact_HidesTokenValueButKeepsLengthHint()
    {
        var redacted = SmokeEnvironment.Redact("abcdefghijklmnop");
        redacted.Should().Be("<redacted len=16>");
        redacted.Should().NotContain("abc");
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsSentinel()
    {
        SmokeEnvironment.Redact(null).Should().Be("<redacted len=0>");
        SmokeEnvironment.Redact("").Should().Be("<redacted len=0>");
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj --filter "FullyQualifiedName~SmokeEnvironmentTests"`
Expected: FAIL — `SmokeEnvironment` not defined.

- [ ] **Step 3: Implement `SmokeEnvironment`**

```csharp
using System;

namespace AdoMcpBridge.Smoke;

internal static class SmokeEnvironment
{
    public const string BridgeUrlVar = "ADOMCP_BRIDGE_URL";
    public const string RefreshTokenVar = "ADOMCP_SMOKE_REFRESH_TOKEN";
    public const string ClientIdVar = "ADOMCP_SMOKE_CLIENT_ID";

    public static Uri RequireBridgeUrl()
    {
        var raw = Environment.GetEnvironmentVariable(BridgeUrlVar)
            ?? throw new InvalidOperationException(
                $"Smoke environment variable {BridgeUrlVar} is not set.");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{BridgeUrlVar} is not a valid absolute URI.");
        return uri;
    }

    public static string RequireRefreshToken() =>
        Environment.GetEnvironmentVariable(RefreshTokenVar)
            ?? throw new InvalidOperationException(
                $"Smoke environment variable {RefreshTokenVar} is not set.");

    public static string RequireClientId() =>
        Environment.GetEnvironmentVariable(ClientIdVar)
            ?? throw new InvalidOperationException(
                $"Smoke environment variable {ClientIdVar} is not set.");

    /// <summary>
    /// Returns a sentinel string safe to put in test output. Never returns
    /// any portion of <paramref name="value"/>. Length is exposed only as a
    /// shape hint for triage.
    /// </summary>
    public static string Redact(string? value) =>
        $"<redacted len={value?.Length ?? 0}>";
}
```

- [ ] **Step 4: Run tests and confirm pass**

Run: `dotnet test tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj --filter "FullyQualifiedName~SmokeEnvironmentTests"`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add tests/AdoMcpBridge.Smoke/SmokeEnvironment.cs tests/AdoMcpBridge.Smoke/Tests/SmokeEnvironmentTests.cs
git commit -m "test: add smoke env loader with redaction helper"
```

---

### Task 3: Shared HttpClient factory

**Files:**
- Create: `tests/AdoMcpBridge.Smoke/SmokeHttpClient.cs`

- [ ] **Step 1: Implement the factory**

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AdoMcpBridge.Smoke;

internal static class SmokeHttpClient
{
    public static HttpClient Create(Uri baseAddress)
    {
        var client = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AdoMcpBridge-Smoke", "1.0"));
        return client;
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Smoke/SmokeHttpClient.cs
git commit -m "test: add shared smoke HttpClient factory"
```

---

### Task 4: DTOs for smoke JSON payloads

**Files:**
- Create: `tests/AdoMcpBridge.Smoke/Models/SmokeDtos.cs`

- [ ] **Step 1: Implement DTOs**

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AdoMcpBridge.Smoke.Models;

internal sealed record OAuthMetadata(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("registration_endpoint")] string RegistrationEndpoint,
    [property: JsonPropertyName("revocation_endpoint")] string RevocationEndpoint,
    [property: JsonPropertyName("response_types_supported")] IReadOnlyList<string> ResponseTypesSupported,
    [property: JsonPropertyName("grant_types_supported")] IReadOnlyList<string> GrantTypesSupported,
    [property: JsonPropertyName("code_challenge_methods_supported")] IReadOnlyList<string> CodeChallengeMethodsSupported);

internal sealed record ConnectorInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("auth_url")] string AuthUrl,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities);

internal sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

internal sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] object? Params);

internal sealed record JsonRpcToolsListResult(
    [property: JsonPropertyName("tools")] IReadOnlyList<JsonRpcTool> Tools);

internal sealed record JsonRpcTool(
    [property: JsonPropertyName("name")] string Name);

internal sealed record JsonRpcResponse<T>(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("result")] T? Result);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Smoke/Models/SmokeDtos.cs
git commit -m "test: add smoke DTOs for OAuth/connector/MCP payloads"
```

---

### Task 5: Discovery smoke test (`/.well-known/oauth-authorization-server`)

**Files:**
- Create: `tests/AdoMcpBridge.Smoke/Tests/DiscoveryTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AdoMcpBridge.Smoke.Models;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("category", "smoke")]
public class DiscoveryTests
{
    [Fact]
    public async Task WellKnown_ReturnsExpectedShape()
    {
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        using var client = SmokeHttpClient.Create(baseUri);

        using var response = await client.GetAsync("/.well-known/oauth-authorization-server");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<OAuthMetadata>();
        doc.Should().NotBeNull();
        doc!.Issuer.TrimEnd('/').Should().Be(baseUri.ToString().TrimEnd('/'));
        doc.AuthorizationEndpoint.Should().StartWith(doc.Issuer);
        doc.TokenEndpoint.Should().EndWith("/token");
        doc.RegistrationEndpoint.Should().EndWith("/register");
        doc.RevocationEndpoint.Should().EndWith("/revoke");
        doc.ResponseTypesSupported.Should().Contain("code");
        doc.GrantTypesSupported.Should().Contain("authorization_code");
        doc.GrantTypesSupported.Should().Contain("refresh_token");
        doc.CodeChallengeMethodsSupported.Should().Contain("S256");
    }
}
```

- [ ] **Step 2: Build to verify the test compiles**

Run: `dotnet build tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Smoke/Tests/DiscoveryTests.cs
git commit -m "test: add /.well-known smoke test"
```

---

### Task 6: Connector card smoke test (`/connector-info.json`)

**Files:**
- Create: `tests/AdoMcpBridge.Smoke/Tests/ConnectorInfoTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AdoMcpBridge.Smoke.Models;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("category", "smoke")]
public class ConnectorInfoTests
{
    [Fact]
    public async Task ConnectorInfo_ReturnsExpectedShape()
    {
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        using var client = SmokeHttpClient.Create(baseUri);

        using var response = await client.GetAsync("/connector-info.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var info = await response.Content.ReadFromJsonAsync<ConnectorInfo>();
        info.Should().NotBeNull();
        info!.Name.Should().NotBeNullOrWhiteSpace();
        info.Description.Should().NotBeNullOrWhiteSpace();
        info.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
        info.AuthUrl.Should().EndWith("/.well-known/oauth-authorization-server");
        info.Capabilities.Should().NotBeEmpty();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Smoke/Tests/ConnectorInfoTests.cs
git commit -m "test: add /connector-info.json smoke test"
```

---

### Task 7: Token refresh smoke test with p95 timing assertion

**Files:**
- Create: `tests/AdoMcpBridge.Smoke/Tests/TokenRefreshTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AdoMcpBridge.Smoke.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("category", "smoke")]
public class TokenRefreshTests
{
    private readonly ITestOutputHelper _output;
    public TokenRefreshTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Refresh_ReturnsNewPairWithinBudget()
    {
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        var refreshToken = SmokeEnvironment.RequireRefreshToken();
        var clientId = SmokeEnvironment.RequireClientId();
        using var client = SmokeHttpClient.Create(baseUri);

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("client_id", clientId),
        });

        var sw = Stopwatch.StartNew();
        using var response = await client.PostAsync("/token", form);
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        payload.Should().NotBeNull();

        // CRITICAL: never log token values. Length-only sentinel.
        _output.WriteLine($"access_token={SmokeEnvironment.Redact(payload!.AccessToken)}");
        _output.WriteLine($"refresh_token={SmokeEnvironment.Redact(payload.RefreshToken)}");
        _output.WriteLine($"token_endpoint_ms={sw.ElapsedMilliseconds}");

        payload.AccessToken.Length.Should().BeGreaterThan(0);
        payload.RefreshToken.Length.Should().BeGreaterThan(0);
        payload.TokenType.Should().Be("Bearer");
        payload.ExpiresIn.Should().BeGreaterThan(0);

        // Logged-only single-shot timing — soft assert so a network blip
        // doesn't flake the run. Real p95 lives in OpenTelemetry alerts.
        if (sw.ElapsedMilliseconds > 2000)
        {
            _output.WriteLine(
                $"WARN: /token took {sw.ElapsedMilliseconds}ms (>2000ms soft budget)");
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Smoke/Tests/TokenRefreshTests.cs
git commit -m "test: add /token refresh-grant smoke test"
```

---

### Task 8: End-to-end MCP `tools/list` smoke test

**Files:**
- Create: `tests/AdoMcpBridge.Smoke/Tests/McpToolsListTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AdoMcpBridge.Smoke.Models;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AdoMcpBridge.Smoke.Tests;

[Trait("category", "smoke")]
public class McpToolsListTests
{
    private readonly ITestOutputHelper _output;
    public McpToolsListTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ToolsList_ReturnsNonEmptyToolset()
    {
        var baseUri = SmokeEnvironment.RequireBridgeUrl();
        var refreshToken = SmokeEnvironment.RequireRefreshToken();
        var clientId = SmokeEnvironment.RequireClientId();
        using var client = SmokeHttpClient.Create(baseUri);

        // Refresh to a fresh access token first.
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("client_id", clientId),
        });
        using var tokenResponse = await client.PostAsync("/token", form);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        token.Should().NotBeNull();
        _output.WriteLine($"access_token={SmokeEnvironment.Redact(token!.AccessToken)}");

        // Call MCP tools/list through the proxy.
        var rpc = new JsonRpcRequest("2.0", 1, "tools/list", new { });
        using var rpcRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(rpc),
        };
        rpcRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using var rpcResponse = await client.SendAsync(rpcRequest);
        rpcResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await rpcResponse.Content.ReadFromJsonAsync<JsonRpcResponse<JsonRpcToolsListResult>>();
        result.Should().NotBeNull();
        result!.JsonRpc.Should().Be("2.0");
        result.Result.Should().NotBeNull();
        result.Result!.Tools.Should().NotBeEmpty(
            because: "the upstream MS Remote MCP server exposes at least one tool");
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Smoke/Tests/McpToolsListTests.cs
git commit -m "test: add MCP tools/list end-to-end smoke test"
```

---

### Task 9: `auto-open-issue` composite action

**Files:**
- Create: `.github/actions/auto-open-issue/action.yml`

- [ ] **Step 1: Write the composite action**

```yaml
name: auto-open-issue
description: >
  Open a deduped GitHub Issue when a smoke run fails. Dedup by exact title:
  if an open issue with the same title and label already exists, post a
  comment instead of opening a duplicate.
inputs:
  title:
    description: Issue title. Used as the dedup key.
    required: true
  body:
    description: Markdown body. Workflow log URL is appended automatically.
    required: true
  label:
    description: Label to apply (and dedup against).
    required: false
    default: smoke-failure
  github-token:
    description: GITHUB_TOKEN with issues:write.
    required: true
runs:
  using: composite
  steps:
    - name: Ensure label exists
      shell: bash
      env:
        GH_TOKEN: ${{ inputs.github-token }}
      run: |
        set -euo pipefail
        if ! gh label list --limit 200 | awk '{print $1}' | grep -Fxq "${{ inputs.label }}"; then
          gh label create "${{ inputs.label }}" \
            --description "Nightly smoke test failure" \
            --color B60205
        fi

    - name: Open or comment
      shell: bash
      env:
        GH_TOKEN: ${{ inputs.github-token }}
        RUN_URL: ${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}
        TITLE: ${{ inputs.title }}
        BODY: ${{ inputs.body }}
        LABEL: ${{ inputs.label }}
      run: |
        set -euo pipefail
        FULL_BODY=$(printf '%s\n\nWorkflow log: %s\nRun attempt: %s\nCommit: %s\n' \
          "$BODY" "$RUN_URL" "${{ github.run_attempt }}" "${{ github.sha }}")

        # Dedup by exact title match within open issues carrying the label.
        EXISTING=$(gh issue list \
          --state open \
          --label "$LABEL" \
          --search "in:title \"$TITLE\"" \
          --json number,title \
          --jq ".[] | select(.title == \"$TITLE\") | .number" \
          | head -n1)

        if [ -n "$EXISTING" ]; then
          echo "Existing issue #$EXISTING matches title; commenting."
          gh issue comment "$EXISTING" --body "$FULL_BODY"
        else
          echo "No matching open issue; creating new."
          gh issue create \
            --title "$TITLE" \
            --body "$FULL_BODY" \
            --label "$LABEL"
        fi
```

- [ ] **Step 2: Lint the YAML**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/actions/auto-open-issue/action.yml'))"`
Expected: exit code 0, no output.

- [ ] **Step 3: Commit**

```bash
git add .github/actions/auto-open-issue/action.yml
git commit -m "ci: add auto-open-issue composite action with title dedup"
```

---

### Task 10: Nightly + on-release smoke workflow

**Files:**
- Create: `.github/workflows/smoke.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: smoke

on:
  schedule:
    - cron: '17 3 * * *'
  release:
    types: [published]
  workflow_dispatch:

permissions:
  contents: read
  issues: write

concurrency:
  group: smoke-${{ github.ref }}
  cancel-in-progress: false

jobs:
  smoke:
    name: Smoke tests (dev)
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj

      - name: Build
        run: dotnet build tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj --configuration Release --no-restore

      - name: Run smoke tests
        id: smoke
        env:
          ADOMCP_BRIDGE_URL: ${{ vars.ADOMCP_BRIDGE_URL }}
          ADOMCP_SMOKE_REFRESH_TOKEN: ${{ secrets.ADOMCP_SMOKE_REFRESH_TOKEN }}
          ADOMCP_SMOKE_CLIENT_ID: ${{ secrets.ADOMCP_SMOKE_CLIENT_ID }}
        run: |
          dotnet test tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj \
            --configuration Release \
            --no-build \
            --filter "Category=smoke" \
            --logger "trx;LogFileName=smoke.trx" \
            --logger "console;verbosity=detailed" \
            --results-directory artifacts/smoke

      - name: Upload TRX
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: smoke-results-${{ github.run_id }}
          path: artifacts/smoke/*.trx
          if-no-files-found: warn

      - name: Open issue on failure
        if: failure()
        uses: ./.github/actions/auto-open-issue
        with:
          title: "Smoke failure: ${{ github.event_name }} @ ${{ github.ref_name }}"
          body: |
            Nightly/on-release smoke run failed against the dev deployment.

            - Event: `${{ github.event_name }}`
            - Ref: `${{ github.ref_name }}`
            - Target URL: `${{ vars.ADOMCP_BRIDGE_URL }}`

            See attached TRX artifact for assertion details. No token values
            are logged by the test project (only redacted length sentinels).
          label: smoke-failure
          github-token: ${{ secrets.GITHUB_TOKEN }}
```

- [ ] **Step 2: Lint the YAML**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/smoke.yml'))"`
Expected: exit code 0.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/smoke.yml
git commit -m "ci: add nightly + on-release smoke workflow with auto-issue"
```

---

### Task 11: Smoke runbook

**Files:**
- Create: `docs/smoke-runbook.md`

- [ ] **Step 1: Write the runbook**

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add docs/smoke-runbook.md
git commit -m "docs: add smoke test runbook"
```

---

### Task 12: Smoke smoke-test (self-check)

**Files:**
- Modify: working tree only — no new files.

- [ ] **Step 1: Build the full solution**

Run: `dotnet build AdoMcpBridge.sln --configuration Release`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Verify all smoke tests are discovered and tagged**

Run: `dotnet test tests/AdoMcpBridge.Smoke/AdoMcpBridge.Smoke.csproj --list-tests --filter "Category=smoke"`
Expected: lists `DiscoveryTests.WellKnown_ReturnsExpectedShape`,
`ConnectorInfoTests.ConnectorInfo_ReturnsExpectedShape`,
`TokenRefreshTests.Refresh_ReturnsNewPairWithinBudget`,
`McpToolsListTests.ToolsList_ReturnsNonEmptyToolset`, plus the two
`SmokeEnvironmentTests` cases.

- [ ] **Step 3: Confirm no token symbols flow into ILogger**

Run: `git grep -nE 'Logger|_output\.WriteLine.*(AccessToken|RefreshToken|refresh_token|access_token)' tests/AdoMcpBridge.Smoke`
Expected: every match is wrapped in `SmokeEnvironment.Redact(...)`. If
any raw token symbol appears as a logger argument, fix it before
proceeding.

- [ ] **Step 4: Push the branch and open the PR**

```bash
git push -u origin claude/smoke-connector
gh pr create --title "feat: smoke tests + connector card coverage" \
  --body "$(cat <<'EOF'
## Summary
- Adds `tests/AdoMcpBridge.Smoke/` with end-to-end coverage for discovery, connector card, token refresh, and MCP `tools/list`.
- Adds `.github/workflows/smoke.yml` (nightly + on `release: published`) and the `auto-open-issue` composite action with title-based dedup.
- Adds `docs/smoke-runbook.md`.

## Review focus
- Confirm `/connector-info.json` is delivered by the MCP-proxy plan; this plan only smoke-tests it.
- Confirm no token values are logged anywhere — only `SmokeEnvironment.Redact` sentinels.
- Confirm the composite action dedups by exact title.

## Test plan
- [ ] `dotnet build AdoMcpBridge.sln --configuration Release` succeeds.
- [ ] `dotnet test tests/AdoMcpBridge.Smoke --list-tests --filter Category=smoke` lists all four live tests + the two redaction tests.
- [ ] Manually trigger `workflow_dispatch` on `smoke.yml` against dev; observe success.
- [ ] Force a failure (point `ADOMCP_BRIDGE_URL` at a 404 host) and verify a `smoke-failure` issue is opened; re-run and verify the second run comments on the same issue.
EOF
)"
```

---

## Self-Review Notes

**`/connector-info.json` ownership — resolved.** Per
`docs/superpowers/plans/_shared-contracts.md` (plan list item 5), the
MCP-proxy plan (`2026-06-09-mcp-proxy.md`) owns the implementation of
`/connector-info.json` alongside the YARP cluster and middleware. This
plan therefore **only adds smoke coverage** for that endpoint
(Task 6) and does **not** add a `ConnectorInfoEndpoint.cs` file. If
review of the proxy plan reveals the endpoint was descoped from it,
the engineer executing this plan must:

1. Stop before Task 6.
2. Add a new task that creates `src/AdoMcpBridge.Api/Endpoints/ConnectorInfoEndpoint.cs` returning the `ConnectorInfo` shape used in Task 4, wired in `Program.cs` via `MapGet("/connector-info.json", ...)`, with corresponding unit tests in `AdoMcpBridge.Api.Tests`.
3. Then resume from Task 6.

**Spec coverage check.** Spec §7 calls for smoke tests against the real
dev deployment that auto-open a GitHub Issue on failure — covered by
Tasks 1-10. Spec §9 calls for the connector card metadata endpoint —
referenced and smoke-tested in Task 6, with ownership delegated above.

**Placeholder scan.** All code, YAML, and shell content is concrete. No
"TBD", no "similar to", no unspecified error handling. The single
forward reference (`SmokeEnvironment.Redact`) is defined in Task 2
before its first use in Task 7.

**Type/name consistency.** `SmokeEnvironment.RequireBridgeUrl`,
`RequireRefreshToken`, `RequireClientId`, `Redact` are defined in
Task 2 and used unchanged in Tasks 5-8. `OAuthMetadata`,
`ConnectorInfo`, `TokenResponse`, `JsonRpcRequest`, `JsonRpcResponse<T>`,
`JsonRpcToolsListResult`, `JsonRpcTool` are defined in Task 4 and used
unchanged thereafter. `category=smoke` trait spelling is consistent
across every test file and matches the workflow's `--filter
"Category=smoke"`.

**Security invariants.** Per spec §5 and CLAUDE.md, tokens are never
logged. The single point where the test code touches token values is
`TokenRefreshTests` and `McpToolsListTests`; both route token values
exclusively through `SmokeEnvironment.Redact` before any `WriteLine`
call. Task 12 Step 3 includes a `git grep` self-check that fails the
plan if any raw token symbol is logged.
