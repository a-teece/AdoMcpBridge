# Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay down the solution skeleton, build properties, two Roslyn analyzers, shared Core abstractions, a `SystemClock`, and a hand-written in-memory `ITokenStore` test double — providing the foundation every later plan depends on.

**Architecture:** `Directory.Build.props` + `.editorconfig` pin `net10.0`, nullable-on, warnings-as-errors, latest LangVersion. `AdoMcpBridge.Core` holds pure abstractions and records (no ASP.NET deps). `AdoMcpBridge.Analyzers` (netstandard2.0) ships diagnostics `ADOMCP001` and `ADOMCP002`. An in-memory `ITokenStore` lives only in `tests/AdoMcpBridge.Core.Tests` (not production). Coverlet is wired so `dotnet test /p:CollectCoverage=true /p:Threshold=100 /p:ThresholdType=line,branch,method` works against Core today.

**Tech Stack:** .NET 10 SDK, C# latest, xUnit, NSubstitute, coverlet.msbuild, Microsoft.CodeAnalysis.CSharp 4.x, Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit.

---

### Task 1: Branch + repo-root build properties

**Files:**
- Create: `Directory.Build.props`
- Create: `.editorconfig`
- Create: `global.json`

- [ ] **Step 1: Create feature branch from latest main**

Run:
```bash
git checkout main && git pull origin main
git checkout -b claude/foundation-skeleton
```
Expected: new branch created, clean tree.

- [ ] **Step 2: Write `global.json`**

Create `/home/user/AdoMcpBridge/global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

- [ ] **Step 3: Write `Directory.Build.props`**

Create `/home/user/AdoMcpBridge/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <DebugType>portable</DebugType>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Write `.editorconfig`**

Create `/home/user/AdoMcpBridge/.editorconfig`:

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
indent_style = space
indent_size = 4
trim_trailing_whitespace = true
insert_final_newline = true

[*.{json,yml,yaml,xml,csproj,props,targets}]
indent_size = 2

[*.cs]
dotnet_sort_system_directives_first = true
csharp_new_line_before_open_brace = all
csharp_style_namespace_declarations = file_scoped:warning
dotnet_diagnostic.IDE0005.severity = warning
```

- [ ] **Step 5: Verify SDK resolves**

Run: `dotnet --version`
Expected: prints a `10.x` version.

- [ ] **Step 6: Commit**

```bash
git add global.json Directory.Build.props .editorconfig
git commit -m "chore: pin .NET 10 SDK and repo-wide build properties"
```

---

### Task 2: Create solution + empty `AdoMcpBridge.Core` project

**Files:**
- Create: `AdoMcpBridge.sln`
- Create: `src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`

- [ ] **Step 1: Create solution and project**

Run:
```bash
cd /home/user/AdoMcpBridge
dotnet new sln -n AdoMcpBridge
mkdir -p src/AdoMcpBridge.Core
dotnet new classlib -n AdoMcpBridge.Core -o src/AdoMcpBridge.Core --framework net10.0
rm src/AdoMcpBridge.Core/Class1.cs
dotnet sln add src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj
```

- [ ] **Step 2: Replace `AdoMcpBridge.Core.csproj` with minimal content**

Overwrite `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>AdoMcpBridge.Core</RootNamespace>
    <AssemblyName>AdoMcpBridge.Core</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Build it**

Run: `dotnet build src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj -v minimal`
Expected: build succeeds with 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add AdoMcpBridge.sln src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj
git commit -m "chore: add solution and empty AdoMcpBridge.Core project"
```

---

### Task 3: Add `AdoMcpBridge.Api` and `AdoMcpBridge.Analyzers` projects

**Files:**
- Create: `src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj`
- Create: `src/AdoMcpBridge.Api/Program.cs`
- Create: `src/AdoMcpBridge.Analyzers/AdoMcpBridge.Analyzers.csproj`
- Create: `src/AdoMcpBridge.Analyzers/AnalyzerReleases.Shipped.md`
- Create: `src/AdoMcpBridge.Analyzers/AnalyzerReleases.Unshipped.md`

- [ ] **Step 1: Create API project skeleton**

Run:
```bash
mkdir -p src/AdoMcpBridge.Api
```

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>AdoMcpBridge.Api</RootNamespace>
    <AssemblyName>AdoMcpBridge.Api</AssemblyName>
    <IsPackable>false</IsPackable>
    <UserSecretsId>adomcp-bridge-api</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AdoMcpBridge.Core\AdoMcpBridge.Core.csproj" />
  </ItemGroup>
</Project>
```

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Api/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok("ok"));
await app.RunAsync();
```

- [ ] **Step 2: Create Analyzers project**

Run:
```bash
mkdir -p src/AdoMcpBridge.Analyzers
```

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Analyzers/AdoMcpBridge.Analyzers.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>AdoMcpBridge.Analyzers</RootNamespace>
    <AssemblyName>AdoMcpBridge.Analyzers</AssemblyName>
    <IsPackable>false</IsPackable>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <AdditionalFiles Include="AnalyzerReleases.Shipped.md" />
    <AdditionalFiles Include="AnalyzerReleases.Unshipped.md" />
  </ItemGroup>
</Project>
```

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Analyzers/AnalyzerReleases.Shipped.md`:

```markdown
; Shipped analyzer releases
```

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Analyzers/AnalyzerReleases.Unshipped.md`:

```markdown
; Unshipped analyzer release
### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|------
ADOMCP001 | Security | Error | NoTokenIntoLoggerAnalyzer: forbids tokens/codes/PKCE verifiers being passed to ILogger
ADOMCP002 | Coverage | Error | ExcludeFromCoverageJustificationAnalyzer: requires non-empty Justification
```

- [ ] **Step 3: Add both to the solution**

Run:
```bash
dotnet sln add src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj src/AdoMcpBridge.Analyzers/AdoMcpBridge.Analyzers.csproj
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build AdoMcpBridge.sln -v minimal`
Expected: build succeeds with 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Api src/AdoMcpBridge.Analyzers AdoMcpBridge.sln
git commit -m "chore: add empty Api and Analyzers projects"
```

---

### Task 4: Add three test projects with xUnit + NSubstitute + coverlet

**Files:**
- Create: `tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`
- Create: `tests/AdoMcpBridge.Core.Tests/Usings.cs`
- Create: `tests/AdoMcpBridge.Api.Tests/AdoMcpBridge.Api.Tests.csproj`
- Create: `tests/AdoMcpBridge.Api.Tests/Usings.cs`
- Create: `tests/AdoMcpBridge.Analyzers.Tests/AdoMcpBridge.Analyzers.Tests.csproj`
- Create: `tests/AdoMcpBridge.Analyzers.Tests/Usings.cs`
- Create: `Directory.Build.targets`

- [ ] **Step 1: Create Core.Tests project**

Run:
```bash
mkdir -p tests/AdoMcpBridge.Core.Tests
```

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>AdoMcpBridge.Core.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
    <PackageReference Include="coverlet.msbuild" Version="6.0.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\AdoMcpBridge.Core\AdoMcpBridge.Core.csproj" />
  </ItemGroup>
</Project>
```

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Core.Tests/Usings.cs`:

```csharp
global using Xunit;
global using NSubstitute;
global using AdoMcpBridge.Core.Abstractions;
```

- [ ] **Step 2: Create Api.Tests project**

Run:
```bash
mkdir -p tests/AdoMcpBridge.Api.Tests
```

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Api.Tests/AdoMcpBridge.Api.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>AdoMcpBridge.Api.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\AdoMcpBridge.Api\AdoMcpBridge.Api.csproj" />
  </ItemGroup>
</Project>
```

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Api.Tests/Usings.cs`:

```csharp
global using Xunit;
global using NSubstitute;
```

- [ ] **Step 3: Create Analyzers.Tests project**

Run:
```bash
mkdir -p tests/AdoMcpBridge.Analyzers.Tests
```

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Analyzers.Tests/AdoMcpBridge.Analyzers.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>AdoMcpBridge.Analyzers.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit" Version="1.1.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\AdoMcpBridge.Analyzers\AdoMcpBridge.Analyzers.csproj" />
  </ItemGroup>
</Project>
```

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Analyzers.Tests/Usings.cs`:

```csharp
global using Xunit;
```

- [ ] **Step 4: Add coverlet defaults via `Directory.Build.targets`**

Create `/home/user/AdoMcpBridge/Directory.Build.targets`:

```xml
<Project>
  <PropertyGroup Condition="'$(IsTestProject)' == 'true'">
    <CollectCoverage Condition="'$(CollectCoverage)' == ''">false</CollectCoverage>
    <CoverletOutputFormat>cobertura</CoverletOutputFormat>
    <CoverletOutput>$(MSBuildThisFileDirectory)artifacts/coverage/$(MSBuildProjectName)/</CoverletOutput>
    <Exclude>[xunit.*]*,[*.Tests]*</Exclude>
    <ExcludeByAttribute>Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute</ExcludeByAttribute>
    <DeterministicSourcePaths>false</DeterministicSourcePaths>
    <SkipAutoProps>true</SkipAutoProps>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Add all three test projects to the solution and build**

Run:
```bash
dotnet sln add tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj tests/AdoMcpBridge.Api.Tests/AdoMcpBridge.Api.Tests.csproj tests/AdoMcpBridge.Analyzers.Tests/AdoMcpBridge.Analyzers.Tests.csproj
dotnet build AdoMcpBridge.sln -v minimal
```
Expected: build succeeds with 0 warnings, 0 errors.

- [ ] **Step 6: Verify `dotnet test` runs (zero tests)**

Run: `dotnet test AdoMcpBridge.sln --no-build`
Expected: succeeds with "Total tests: 0" across three test projects (no failures).

- [ ] **Step 7: Commit**

```bash
git add tests Directory.Build.targets AdoMcpBridge.sln
git commit -m "chore: add xUnit/NSubstitute/coverlet test projects"
```

---

### Task 5: `IClock` abstraction + `SystemClock` (TDD)

**Files:**
- Create: `src/AdoMcpBridge.Core/Abstractions/IClock.cs`
- Create: `src/AdoMcpBridge.Core/Time/SystemClock.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/Time/SystemClockTests.cs`

- [ ] **Step 1: Write failing test**

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Core.Tests/Time/SystemClockTests.cs`:

```csharp
using AdoMcpBridge.Core.Time;

namespace AdoMcpBridge.Core.Tests.Time;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_returns_value_within_one_second_of_DateTimeOffset_UtcNow()
    {
        IClock clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;
        var now = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.True(now >= before.AddSeconds(-1));
        Assert.True(now <= after.AddSeconds(1));
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }
}
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~SystemClockTests" -v normal`
Expected: FAIL — `IClock` / `SystemClock` not found (compile error).

- [ ] **Step 3: Implement `IClock`**

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Abstractions/IClock.cs`:

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

- [ ] **Step 4: Implement `SystemClock`**

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Time/SystemClock.cs`:

```csharp
using AdoMcpBridge.Core.Abstractions;

namespace AdoMcpBridge.Core.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

- [ ] **Step 5: Run to pass**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~SystemClockTests" -v normal`
Expected: PASS, 1 test.

- [ ] **Step 6: Commit**

```bash
git add src/AdoMcpBridge.Core/Abstractions/IClock.cs src/AdoMcpBridge.Core/Time/SystemClock.cs tests/AdoMcpBridge.Core.Tests/Time/SystemClockTests.cs
git commit -m "feat: add IClock abstraction and SystemClock"
```

---

### Task 6: Encryption + Entra token abstractions

**Files:**
- Create: `src/AdoMcpBridge.Core/Abstractions/IKeyVaultEncryptor.cs`
- Create: `src/AdoMcpBridge.Core/Abstractions/IEntraTokenClient.cs`
- Create: `src/AdoMcpBridge.Core/Abstractions/EntraTokenResult.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/Abstractions/EntraTokenResultTests.cs`

- [ ] **Step 1: Write failing test for the record's value semantics**

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Core.Tests/Abstractions/EntraTokenResultTests.cs`:

```csharp
namespace AdoMcpBridge.Core.Tests.Abstractions;

public sealed class EntraTokenResultTests
{
    [Fact]
    public void Two_equal_instances_compare_equal()
    {
        var expires = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        var a = new EntraTokenResult("at", "rt", expires, "oid", "upn@x");
        var b = new EntraTokenResult("at", "rt", expires, "oid", "upn@x");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Properties_round_trip()
    {
        var expires = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        var r = new EntraTokenResult("at", "rt", expires, "oid", "upn@x");
        Assert.Equal("at", r.AccessToken);
        Assert.Equal("rt", r.RefreshToken);
        Assert.Equal(expires, r.ExpiresAt);
        Assert.Equal("oid", r.UserObjectId);
        Assert.Equal("upn@x", r.UserPrincipalName);
    }
}
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~EntraTokenResultTests" -v normal`
Expected: FAIL — `EntraTokenResult` not found (compile error).

- [ ] **Step 3: Implement `IKeyVaultEncryptor`**

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Abstractions/IKeyVaultEncryptor.cs`:

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public interface IKeyVaultEncryptor
{
    ValueTask<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct);
    ValueTask<byte[]> DecryptAsync(byte[] ciphertext, CancellationToken ct);
}
```

- [ ] **Step 4: Implement `EntraTokenResult` and `IEntraTokenClient`**

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Abstractions/EntraTokenResult.cs`:

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public sealed record EntraTokenResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string UserObjectId,
    string UserPrincipalName);
```

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Abstractions/IEntraTokenClient.cs`:

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public interface IEntraTokenClient
{
    ValueTask<EntraTokenResult> ExchangeAuthorizationCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct);

    ValueTask<EntraTokenResult> AcquireAdoTokenAsync(
        string entraRefreshToken, CancellationToken ct);
}
```

- [ ] **Step 5: Run to pass**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~EntraTokenResultTests" -v normal`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add src/AdoMcpBridge.Core/Abstractions tests/AdoMcpBridge.Core.Tests/Abstractions
git commit -m "feat: add IKeyVaultEncryptor and IEntraTokenClient abstractions"
```

---

### Task 7: Token store records (`RegisteredClient`, `AuthorizationCodeRecord`, `TokenRecord`)

**Files:**
- Create: `src/AdoMcpBridge.Core/Abstractions/RegisteredClient.cs`
- Create: `src/AdoMcpBridge.Core/Abstractions/AuthorizationCodeRecord.cs`
- Create: `src/AdoMcpBridge.Core/Abstractions/TokenRecord.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/Abstractions/TokenRecordsTests.cs`

- [ ] **Step 1: Write failing test**

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Core.Tests/Abstractions/TokenRecordsTests.cs`:

```csharp
namespace AdoMcpBridge.Core.Tests.Abstractions;

public sealed class TokenRecordsTests
{
    [Fact]
    public void RegisteredClient_holds_all_properties()
    {
        var created = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        var rc = new RegisteredClient("cid", "name", new[] { "https://x/cb" }, created);
        Assert.Equal("cid", rc.ClientId);
        Assert.Equal("name", rc.ClientName);
        Assert.Single(rc.RedirectUris);
        Assert.Equal("https://x/cb", rc.RedirectUris[0]);
        Assert.Equal(created, rc.CreatedAt);
    }

    [Fact]
    public void AuthorizationCodeRecord_holds_all_properties()
    {
        var expires = new DateTimeOffset(2026, 6, 9, 0, 1, 0, TimeSpan.Zero);
        var ac = new AuthorizationCodeRecord(
            "code", "cid", "https://x/cb", "challenge", "S256",
            "ZW5j", "oid", "upn@x", expires);
        Assert.Equal("code", ac.Code);
        Assert.Equal("cid", ac.ClientId);
        Assert.Equal("https://x/cb", ac.RedirectUri);
        Assert.Equal("challenge", ac.PkceChallenge);
        Assert.Equal("S256", ac.PkceMethod);
        Assert.Equal("ZW5j", ac.EntraRefreshTokenEncrypted);
        Assert.Equal("oid", ac.UserObjectId);
        Assert.Equal("upn@x", ac.UserPrincipalName);
        Assert.Equal(expires, ac.ExpiresAt);
    }

    [Fact]
    public void TokenRecord_holds_all_properties()
    {
        var atExp = new DateTimeOffset(2026, 6, 9, 1, 0, 0, TimeSpan.Zero);
        var rtExp = new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero);
        var created = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        var tr = new TokenRecord(
            "ah", "rh", "cid", "ZW5j", "oid", "upn@x", atExp, rtExp, created);
        Assert.Equal("ah", tr.AccessTokenHash);
        Assert.Equal("rh", tr.RefreshTokenHash);
        Assert.Equal("cid", tr.ClientId);
        Assert.Equal("ZW5j", tr.EntraRefreshTokenEncrypted);
        Assert.Equal("oid", tr.UserObjectId);
        Assert.Equal("upn@x", tr.UserPrincipalName);
        Assert.Equal(atExp, tr.AccessTokenExpiresAt);
        Assert.Equal(rtExp, tr.RefreshTokenExpiresAt);
        Assert.Equal(created, tr.CreatedAt);
    }
}
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~TokenRecordsTests" -v normal`
Expected: FAIL — `RegisteredClient` / `AuthorizationCodeRecord` / `TokenRecord` not found.

- [ ] **Step 3: Implement records**

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Abstractions/RegisteredClient.cs`:

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public sealed record RegisteredClient(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    DateTimeOffset CreatedAt);
```

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Abstractions/AuthorizationCodeRecord.cs`:

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public sealed record AuthorizationCodeRecord(
    string Code,
    string ClientId,
    string RedirectUri,
    string PkceChallenge,
    string PkceMethod,
    string EntraRefreshTokenEncrypted,
    string UserObjectId,
    string UserPrincipalName,
    DateTimeOffset ExpiresAt);
```

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Abstractions/TokenRecord.cs`:

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public sealed record TokenRecord(
    string AccessTokenHash,
    string RefreshTokenHash,
    string ClientId,
    string EntraRefreshTokenEncrypted,
    string UserObjectId,
    string UserPrincipalName,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 4: Run to pass**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~TokenRecordsTests" -v normal`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Abstractions tests/AdoMcpBridge.Core.Tests/Abstractions
git commit -m "feat: add RegisteredClient, AuthorizationCodeRecord, TokenRecord"
```

---

### Task 8: `ITokenStore` interface

**Files:**
- Create: `src/AdoMcpBridge.Core/Abstractions/ITokenStore.cs`

- [ ] **Step 1: Create the interface**

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/Abstractions/ITokenStore.cs`:

```csharp
namespace AdoMcpBridge.Core.Abstractions;

public interface ITokenStore
{
    ValueTask<RegisteredClient?> FindClientAsync(string clientId, CancellationToken ct);
    ValueTask AddClientAsync(RegisteredClient client, CancellationToken ct);

    ValueTask AddAuthorizationCodeAsync(AuthorizationCodeRecord code, CancellationToken ct);
    ValueTask<AuthorizationCodeRecord?> ConsumeAuthorizationCodeAsync(string code, CancellationToken ct);

    ValueTask AddTokenAsync(TokenRecord token, CancellationToken ct);
    ValueTask<TokenRecord?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct);
    ValueTask<TokenRecord?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct);
    ValueTask RevokeTokenAsync(string refreshTokenHash, CancellationToken ct);
    ValueTask ReplaceTokenAsync(TokenRecord oldToken, TokenRecord newToken, CancellationToken ct);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj -v minimal`
Expected: succeeds with 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/AdoMcpBridge.Core/Abstractions/ITokenStore.cs
git commit -m "feat: add ITokenStore interface"
```

---

### Task 9: In-memory `ITokenStore` test double — client + code paths (TDD)

**Files:**
- Create: `tests/AdoMcpBridge.Core.Tests/InMemoryTokenStore.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/InMemoryTokenStoreTests.cs`

- [ ] **Step 1: Write failing tests for client + auth-code behavior**

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Core.Tests/InMemoryTokenStoreTests.cs`:

```csharp
namespace AdoMcpBridge.Core.Tests;

public sealed class InMemoryTokenStoreTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task AddClient_then_FindClient_returns_client()
    {
        var store = new InMemoryTokenStore();
        var client = new RegisteredClient("cid", "name", new[] { "https://x/cb" }, DateTimeOffset.UtcNow);
        await store.AddClientAsync(client, Ct);
        var found = await store.FindClientAsync("cid", Ct);
        Assert.Equal(client, found);
    }

    [Fact]
    public async Task FindClient_unknown_id_returns_null()
    {
        var store = new InMemoryTokenStore();
        Assert.Null(await store.FindClientAsync("missing", Ct));
    }

    [Fact]
    public async Task AddClient_duplicate_id_throws_InvalidOperationException()
    {
        var store = new InMemoryTokenStore();
        var c = new RegisteredClient("cid", "n", Array.Empty<string>(), DateTimeOffset.UtcNow);
        await store.AddClientAsync(c, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddClientAsync(c, Ct).AsTask());
    }

    [Fact]
    public async Task ConsumeAuthorizationCode_returns_then_removes_record()
    {
        var store = new InMemoryTokenStore();
        var rec = new AuthorizationCodeRecord(
            "code", "cid", "https://x/cb", "ch", "S256", "ZW5j", "oid", "upn@x",
            DateTimeOffset.UtcNow.AddSeconds(60));
        await store.AddAuthorizationCodeAsync(rec, Ct);

        var first = await store.ConsumeAuthorizationCodeAsync("code", Ct);
        var second = await store.ConsumeAuthorizationCodeAsync("code", Ct);

        Assert.Equal(rec, first);
        Assert.Null(second);
    }

    [Fact]
    public async Task ConsumeAuthorizationCode_unknown_returns_null()
    {
        var store = new InMemoryTokenStore();
        Assert.Null(await store.ConsumeAuthorizationCodeAsync("missing", Ct));
    }
}
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~InMemoryTokenStoreTests" -v normal`
Expected: FAIL — `InMemoryTokenStore` not found.

- [ ] **Step 3: Implement `InMemoryTokenStore`**

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Core.Tests/InMemoryTokenStore.cs`:

```csharp
using System.Collections.Concurrent;

namespace AdoMcpBridge.Core.Tests;

public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, RegisteredClient> _clients = new();
    private readonly ConcurrentDictionary<string, AuthorizationCodeRecord> _codes = new();
    private readonly ConcurrentDictionary<string, TokenRecord> _tokensByAccessHash = new();

    public ValueTask<RegisteredClient?> FindClientAsync(string clientId, CancellationToken ct)
    {
        _clients.TryGetValue(clientId, out var c);
        return ValueTask.FromResult(c);
    }

    public ValueTask AddClientAsync(RegisteredClient client, CancellationToken ct)
    {
        if (!_clients.TryAdd(client.ClientId, client))
        {
            throw new InvalidOperationException($"Client '{client.ClientId}' already exists.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask AddAuthorizationCodeAsync(AuthorizationCodeRecord code, CancellationToken ct)
    {
        if (!_codes.TryAdd(code.Code, code))
        {
            throw new InvalidOperationException($"Authorization code already present.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<AuthorizationCodeRecord?> ConsumeAuthorizationCodeAsync(string code, CancellationToken ct)
    {
        _codes.TryRemove(code, out var rec);
        return ValueTask.FromResult(rec);
    }

    public ValueTask AddTokenAsync(TokenRecord token, CancellationToken ct)
    {
        if (!_tokensByAccessHash.TryAdd(token.AccessTokenHash, token))
        {
            throw new InvalidOperationException("Access token hash collision.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<TokenRecord?> FindByAccessTokenHashAsync(string accessTokenHash, CancellationToken ct)
    {
        _tokensByAccessHash.TryGetValue(accessTokenHash, out var t);
        return ValueTask.FromResult(t);
    }

    public ValueTask<TokenRecord?> FindByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct)
    {
        TokenRecord? match = null;
        foreach (var t in _tokensByAccessHash.Values)
        {
            if (t.RefreshTokenHash == refreshTokenHash)
            {
                match = t;
                break;
            }
        }
        return ValueTask.FromResult(match);
    }

    public ValueTask RevokeTokenAsync(string refreshTokenHash, CancellationToken ct)
    {
        foreach (var kv in _tokensByAccessHash)
        {
            if (kv.Value.RefreshTokenHash == refreshTokenHash)
            {
                _tokensByAccessHash.TryRemove(kv.Key, out _);
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ReplaceTokenAsync(TokenRecord oldToken, TokenRecord newToken, CancellationToken ct)
    {
        _tokensByAccessHash.TryRemove(oldToken.AccessTokenHash, out _);
        if (!_tokensByAccessHash.TryAdd(newToken.AccessTokenHash, newToken))
        {
            throw new InvalidOperationException("Replacement token hash collision.");
        }
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 4: Run to pass**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~InMemoryTokenStoreTests" -v normal`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add tests/AdoMcpBridge.Core.Tests/InMemoryTokenStore.cs tests/AdoMcpBridge.Core.Tests/InMemoryTokenStoreTests.cs
git commit -m "test: add in-memory ITokenStore test double with client/code tests"
```

---

### Task 10: In-memory `ITokenStore` — token paths (TDD, full coverage)

**Files:**
- Modify: `tests/AdoMcpBridge.Core.Tests/InMemoryTokenStoreTests.cs`

- [ ] **Step 1: Append failing tests covering token add/find/replace/revoke**

Append to `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Core.Tests/InMemoryTokenStoreTests.cs` (inside the existing class, before the closing brace):

```csharp
    private static TokenRecord NewToken(string accessHash, string refreshHash) => new(
        accessHash, refreshHash, "cid", "ZW5j", "oid", "upn@x",
        DateTimeOffset.UtcNow.AddHours(1),
        DateTimeOffset.UtcNow.AddDays(14),
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task AddToken_then_FindByAccessTokenHash_returns_it()
    {
        var store = new InMemoryTokenStore();
        var t = NewToken("ah1", "rh1");
        await store.AddTokenAsync(t, Ct);
        Assert.Equal(t, await store.FindByAccessTokenHashAsync("ah1", Ct));
    }

    [Fact]
    public async Task FindByAccessTokenHash_unknown_returns_null()
    {
        var store = new InMemoryTokenStore();
        Assert.Null(await store.FindByAccessTokenHashAsync("nope", Ct));
    }

    [Fact]
    public async Task AddToken_duplicate_access_hash_throws()
    {
        var store = new InMemoryTokenStore();
        var t = NewToken("ah1", "rh1");
        await store.AddTokenAsync(t, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddTokenAsync(t, Ct).AsTask());
    }

    [Fact]
    public async Task FindByRefreshTokenHash_returns_match_and_null()
    {
        var store = new InMemoryTokenStore();
        await store.AddTokenAsync(NewToken("ah1", "rh1"), Ct);
        await store.AddTokenAsync(NewToken("ah2", "rh2"), Ct);

        var found = await store.FindByRefreshTokenHashAsync("rh2", Ct);
        Assert.NotNull(found);
        Assert.Equal("ah2", found!.AccessTokenHash);

        Assert.Null(await store.FindByRefreshTokenHashAsync("rh3", Ct));
    }

    [Fact]
    public async Task RevokeToken_removes_by_refresh_hash()
    {
        var store = new InMemoryTokenStore();
        await store.AddTokenAsync(NewToken("ah1", "rh1"), Ct);
        await store.RevokeTokenAsync("rh1", Ct);
        Assert.Null(await store.FindByAccessTokenHashAsync("ah1", Ct));
        Assert.Null(await store.FindByRefreshTokenHashAsync("rh1", Ct));
    }

    [Fact]
    public async Task RevokeToken_unknown_hash_is_noop()
    {
        var store = new InMemoryTokenStore();
        await store.AddTokenAsync(NewToken("ah1", "rh1"), Ct);
        await store.RevokeTokenAsync("rh-missing", Ct);
        Assert.NotNull(await store.FindByAccessTokenHashAsync("ah1", Ct));
    }

    [Fact]
    public async Task ReplaceToken_swaps_old_for_new()
    {
        var store = new InMemoryTokenStore();
        var oldT = NewToken("ah1", "rh1");
        var newT = NewToken("ah2", "rh2");
        await store.AddTokenAsync(oldT, Ct);
        await store.ReplaceTokenAsync(oldT, newT, Ct);
        Assert.Null(await store.FindByAccessTokenHashAsync("ah1", Ct));
        Assert.Equal(newT, await store.FindByAccessTokenHashAsync("ah2", Ct));
    }

    [Fact]
    public async Task ReplaceToken_collision_on_new_hash_throws()
    {
        var store = new InMemoryTokenStore();
        var oldT = NewToken("ah1", "rh1");
        var other = NewToken("ah2", "rh2");
        await store.AddTokenAsync(oldT, Ct);
        await store.AddTokenAsync(other, Ct);
        var dupe = NewToken("ah2", "rh3");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceTokenAsync(oldT, dupe, Ct).AsTask());
    }

    [Fact]
    public async Task AddAuthorizationCode_duplicate_throws()
    {
        var store = new InMemoryTokenStore();
        var rec = new AuthorizationCodeRecord(
            "code", "cid", "https://x/cb", "ch", "S256", "ZW5j", "oid", "upn@x",
            DateTimeOffset.UtcNow.AddSeconds(60));
        await store.AddAuthorizationCodeAsync(rec, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAuthorizationCodeAsync(rec, Ct).AsTask());
    }
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~InMemoryTokenStoreTests" -v normal`
Expected: PASS — the implementation written in Task 9 already covers these. (If any FAIL, fix the implementation, not the tests.)

- [ ] **Step 3: Verify 100% coverage on `InMemoryTokenStore` via coverlet**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj /p:CollectCoverage=true /p:Threshold=100 /p:ThresholdType=line,branch,method /p:Include=\"[AdoMcpBridge.Core.Tests]AdoMcpBridge.Core.Tests.InMemoryTokenStore\"`
Expected: succeeds, coverlet prints `Total | 100% | 100% | 100%` for the included type.

- [ ] **Step 4: Commit**

```bash
git add tests/AdoMcpBridge.Core.Tests/InMemoryTokenStoreTests.cs
git commit -m "test: cover token add/find/replace/revoke paths in InMemoryTokenStore"
```

---

### Task 11: Analyzer `ADOMCP001` — no token/code/verifier into `ILogger` (TDD)

**Files:**
- Create: `src/AdoMcpBridge.Analyzers/NoTokenIntoLoggerAnalyzer.cs`
- Create: `tests/AdoMcpBridge.Analyzers.Tests/AnalyzerTestHarness.cs`
- Create: `tests/AdoMcpBridge.Analyzers.Tests/NoTokenIntoLoggerAnalyzerTests.cs`

- [ ] **Step 1: Write the harness once for both analyzers**

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Analyzers.Tests/AnalyzerTestHarness.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace AdoMcpBridge.Analyzers.Tests;

internal static class AnalyzerTestHarness<TAnalyzer> where TAnalyzer : DiagnosticAnalyzer, new()
{
    public sealed class Test : CSharpAnalyzerTest<TAnalyzer, XUnitVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
            TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location));
            TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.LoggerExtensions).Assembly.Location));
        }
    }

    public static Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var t = new Test { TestCode = source };
        t.ExpectedDiagnostics.AddRange(expected);
        return t.RunAsync();
    }
}
```

Add to `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Analyzers.Tests/AdoMcpBridge.Analyzers.Tests.csproj` inside the `<ItemGroup>` containing package references:

```xml
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
```

- [ ] **Step 2: Write failing tests**

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Analyzers.Tests/NoTokenIntoLoggerAnalyzerTests.cs`:

```csharp
using AdoMcpBridge.Analyzers;
using Microsoft.CodeAnalysis.Testing;

namespace AdoMcpBridge.Analyzers.Tests;

public sealed class NoTokenIntoLoggerAnalyzerTests
{
    [Fact]
    public Task Flags_local_named_accessToken_into_LogInformation()
    {
        const string src = """
            using Microsoft.Extensions.Logging;
            class C
            {
                void M(ILogger logger)
                {
                    var accessToken = "secret";
                    logger.LogInformation("got {Tok}", {|#0:accessToken|});
                }
            }
            """;
        var expected = new DiagnosticResult("ADOMCP001", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("accessToken");
        return AnalyzerTestHarness<NoTokenIntoLoggerAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Flags_parameter_named_codeVerifier_into_LogDebug()
    {
        const string src = """
            using Microsoft.Extensions.Logging;
            class C
            {
                void M(ILogger logger, string codeVerifier)
                {
                    logger.LogDebug("v={V}", {|#0:codeVerifier|});
                }
            }
            """;
        var expected = new DiagnosticResult("ADOMCP001", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("codeVerifier");
        return AnalyzerTestHarness<NoTokenIntoLoggerAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Does_not_flag_unrelated_variable()
    {
        const string src = """
            using Microsoft.Extensions.Logging;
            class C
            {
                void M(ILogger logger)
                {
                    var userId = "abc";
                    logger.LogInformation("uid={U}", userId);
                }
            }
            """;
        return AnalyzerTestHarness<NoTokenIntoLoggerAnalyzer>.VerifyAsync(src);
    }

    [Fact]
    public Task Flags_pkce_verifier_property_access()
    {
        const string src = """
            using Microsoft.Extensions.Logging;
            class C
            {
                string PkceVerifier { get; set; } = "";
                void M(ILogger logger)
                {
                    logger.LogInformation("v={V}", {|#0:PkceVerifier|});
                }
            }
            """;
        var expected = new DiagnosticResult("ADOMCP001", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("PkceVerifier");
        return AnalyzerTestHarness<NoTokenIntoLoggerAnalyzer>.VerifyAsync(src, expected);
    }
}
```

- [ ] **Step 3: Run to fail**

Run: `dotnet test tests/AdoMcpBridge.Analyzers.Tests/AdoMcpBridge.Analyzers.Tests.csproj --filter "FullyQualifiedName~NoTokenIntoLoggerAnalyzerTests" -v normal`
Expected: FAIL — `NoTokenIntoLoggerAnalyzer` not found (compile error).

- [ ] **Step 4: Implement the analyzer**

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Analyzers/NoTokenIntoLoggerAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AdoMcpBridge.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoTokenIntoLoggerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ADOMCP001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Do not log tokens, codes, or PKCE verifiers",
        messageFormat: "Symbol '{0}' looks like a secret (token/code/verifier) and must not be passed to ILogger",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Prevents accidental logging of OAuth tokens, authorization codes, or PKCE verifiers.");

    private static readonly string[] ForbiddenSubstrings =
    {
        "accesstoken", "refreshtoken", "idtoken", "bearertoken",
        "authcode", "authorizationcode",
        "codeverifier", "pkceverifier", "pkcecode",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containing = symbol.ContainingType?.ToDisplayString() ?? "";
        if (containing != "Microsoft.Extensions.Logging.LoggerExtensions" &&
            containing != "Microsoft.Extensions.Logging.ILogger")
        {
            return;
        }

        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            var name = ExtractName(arg.Expression);
            if (name is null) continue;
            var lower = name.ToLowerInvariant();
            foreach (var bad in ForbiddenSubstrings)
            {
                if (lower.Contains(bad))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Rule, arg.Expression.GetLocation(), name));
                    break;
                }
            }
        }
    }

    private static string? ExtractName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        _ => null,
    };
}
```

- [ ] **Step 5: Run to pass**

Run: `dotnet test tests/AdoMcpBridge.Analyzers.Tests/AdoMcpBridge.Analyzers.Tests.csproj --filter "FullyQualifiedName~NoTokenIntoLoggerAnalyzerTests" -v normal`
Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/AdoMcpBridge.Analyzers/NoTokenIntoLoggerAnalyzer.cs tests/AdoMcpBridge.Analyzers.Tests src/AdoMcpBridge.Analyzers/AdoMcpBridge.Analyzers.csproj
git commit -m "feat: add ADOMCP001 analyzer forbidding tokens in ILogger"
```

---

### Task 12: Analyzer `ADOMCP002` — `[ExcludeFromCodeCoverage]` requires Justification (TDD)

**Files:**
- Create: `src/AdoMcpBridge.Analyzers/ExcludeFromCoverageJustificationAnalyzer.cs`
- Create: `tests/AdoMcpBridge.Analyzers.Tests/ExcludeFromCoverageJustificationAnalyzerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `/home/user/AdoMcpBridge/tests/AdoMcpBridge.Analyzers.Tests/ExcludeFromCoverageJustificationAnalyzerTests.cs`:

```csharp
using AdoMcpBridge.Analyzers;
using Microsoft.CodeAnalysis.Testing;

namespace AdoMcpBridge.Analyzers.Tests;

public sealed class ExcludeFromCoverageJustificationAnalyzerTests
{
    [Fact]
    public Task Flags_attribute_without_Justification()
    {
        const string src = """
            using System.Diagnostics.CodeAnalysis;
            [{|#0:ExcludeFromCodeCoverage|}]
            class C { }
            """;
        var expected = new DiagnosticResult("ADOMCP002", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0);
        return AnalyzerTestHarness<ExcludeFromCoverageJustificationAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Flags_attribute_with_empty_Justification()
    {
        const string src = """
            using System.Diagnostics.CodeAnalysis;
            [{|#0:ExcludeFromCodeCoverage(Justification = "")|}]
            class C { }
            """;
        var expected = new DiagnosticResult("ADOMCP002", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0);
        return AnalyzerTestHarness<ExcludeFromCoverageJustificationAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Flags_attribute_with_whitespace_Justification()
    {
        const string src = """
            using System.Diagnostics.CodeAnalysis;
            [{|#0:ExcludeFromCodeCoverage(Justification = "   ")|}]
            class C { }
            """;
        var expected = new DiagnosticResult("ADOMCP002", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0);
        return AnalyzerTestHarness<ExcludeFromCoverageJustificationAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Does_not_flag_attribute_with_real_Justification()
    {
        const string src = """
            using System.Diagnostics.CodeAnalysis;
            [ExcludeFromCodeCoverage(Justification = "Program entry point exercised by integration tests only.")]
            class C { }
            """;
        return AnalyzerTestHarness<ExcludeFromCoverageJustificationAnalyzer>.VerifyAsync(src);
    }
}
```

- [ ] **Step 2: Run to fail**

Run: `dotnet test tests/AdoMcpBridge.Analyzers.Tests/AdoMcpBridge.Analyzers.Tests.csproj --filter "FullyQualifiedName~ExcludeFromCoverageJustificationAnalyzerTests" -v normal`
Expected: FAIL — `ExcludeFromCoverageJustificationAnalyzer` not found.

- [ ] **Step 3: Implement the analyzer**

Create `/home/user/AdoMcpBridge/src/AdoMcpBridge.Analyzers/ExcludeFromCoverageJustificationAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AdoMcpBridge.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExcludeFromCoverageJustificationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ADOMCP002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "[ExcludeFromCodeCoverage] requires non-empty Justification",
        messageFormat: "[ExcludeFromCodeCoverage] must specify a non-empty Justification argument",
        category: "Coverage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Enforces audit trail when code is excluded from coverage measurement.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.Attribute);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        var attr = (AttributeSyntax)ctx.Node;
        var typeInfo = ctx.SemanticModel.GetTypeInfo(attr, ctx.CancellationToken);
        var name = typeInfo.Type?.ToDisplayString();
        if (name != "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute")
        {
            return;
        }

        var args = attr.ArgumentList?.Arguments;
        string? justification = null;
        if (args is { } list)
        {
            foreach (var a in list)
            {
                if (a.NameEquals?.Name.Identifier.ValueText == "Justification" &&
                    a.Expression is LiteralExpressionSyntax lit &&
                    lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    justification = lit.Token.ValueText;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, attr.GetLocation()));
        }
    }
}
```

- [ ] **Step 4: Run to pass**

Run: `dotnet test tests/AdoMcpBridge.Analyzers.Tests/AdoMcpBridge.Analyzers.Tests.csproj --filter "FullyQualifiedName~ExcludeFromCoverageJustificationAnalyzerTests" -v normal`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Analyzers/ExcludeFromCoverageJustificationAnalyzer.cs tests/AdoMcpBridge.Analyzers.Tests/ExcludeFromCoverageJustificationAnalyzerTests.cs
git commit -m "feat: add ADOMCP002 analyzer requiring Justification on ExcludeFromCodeCoverage"
```

---

### Task 13: Wire analyzers into `AdoMcpBridge.Core` and verify end-to-end coverage gate

**Files:**
- Modify: `src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`
- Modify: `src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj`

- [ ] **Step 1: Reference the analyzer project as an analyzer from `Core`**

Overwrite `/home/user/AdoMcpBridge/src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>AdoMcpBridge.Core</RootNamespace>
    <AssemblyName>AdoMcpBridge.Core</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AdoMcpBridge.Analyzers\AdoMcpBridge.Analyzers.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Same for `AdoMcpBridge.Api`**

Overwrite `/home/user/AdoMcpBridge/src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>AdoMcpBridge.Api</RootNamespace>
    <AssemblyName>AdoMcpBridge.Api</AssemblyName>
    <IsPackable>false</IsPackable>
    <UserSecretsId>adomcp-bridge-api</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AdoMcpBridge.Core\AdoMcpBridge.Core.csproj" />
    <ProjectReference Include="..\AdoMcpBridge.Analyzers\AdoMcpBridge.Analyzers.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Build the full solution**

Run: `dotnet build AdoMcpBridge.sln -v minimal`
Expected: build succeeds with 0 warnings, 0 errors (analyzers are loaded but flag nothing on current code).

- [ ] **Step 4: Run the full test suite with 100% coverage gate on Core**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj /p:CollectCoverage=true /p:Threshold=100 /p:ThresholdType=line,branch,method /p:Include=\"[AdoMcpBridge.Core]*\"`
Expected: succeeds, coverlet prints `Total | 100% | 100% | 100%`.

- [ ] **Step 5: Run all tests (no coverage gate) to confirm green suite**

Run: `dotnet test AdoMcpBridge.sln -v minimal`
Expected: all test projects pass; total test count >= 18 (1 clock + 5 record + 14 store + 8 analyzer).

- [ ] **Step 6: Commit**

```bash
git add src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj
git commit -m "chore: wire ADOMCP001/002 analyzers into Core and Api projects"
```

---

### Task 14: Push branch and open PR

**Files:**
- None (git remote operations only).

- [ ] **Step 1: Push the branch**

Run:
```bash
git push -u origin claude/foundation-skeleton
```
Expected: push succeeds; GitHub returns PR creation URL.

- [ ] **Step 2: Open PR (ready, not draft)**

Run:
```bash
gh pr create --title "feat: foundation skeleton, analyzers, shared abstractions" --body "$(cat <<'EOF'
## Summary
- Solution skeleton: `AdoMcpBridge.Core`, `AdoMcpBridge.Api`, `AdoMcpBridge.Analyzers`, plus three test projects.
- Repo-wide `Directory.Build.props` (net10.0, nullable, warnings-as-errors, latest LangVersion) and `.editorconfig`.
- Roslyn analyzers `ADOMCP001` (no tokens/codes/verifiers into `ILogger`) and `ADOMCP002` (`[ExcludeFromCodeCoverage]` requires `Justification`), each with full xUnit coverage via `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit`.
- Core abstractions: `IClock` + `SystemClock`, `IKeyVaultEncryptor`, `IEntraTokenClient` + `EntraTokenResult`, `ITokenStore` + `RegisteredClient` / `AuthorizationCodeRecord` / `TokenRecord`.
- Hand-written `InMemoryTokenStore` test double (no EF in-memory) with 100% line/branch/method coverage via coverlet.

## Review focus
- That analyzer false-positive/false-negative cases match the spec's intent (§5 security invariants, §7 coverage gate).
- That contract shapes exactly match `docs/superpowers/plans/_shared-contracts.md`.

## Test plan
- `dotnet build AdoMcpBridge.sln` — clean.
- `dotnet test AdoMcpBridge.sln` — all green.
- `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj /p:CollectCoverage=true /p:Threshold=100 /p:ThresholdType=line,branch,method /p:Include="[AdoMcpBridge.Core]*"` — 100% on Core.
EOF
)"
```
Expected: PR URL printed; report it back in the chat reply.

---

## Self-Review Notes

Cross-checks performed against the spec, shared contracts, and task scope:

1. **Spec coverage (in-scope items only):**
   - Repo-root `Directory.Build.props` + `.editorconfig` with `net10.0`, nullable, warnings-as-errors, latest LangVersion — Task 1. ✓
   - Solution + `AdoMcpBridge.Core` / `.Api` / `.Analyzers` projects — Tasks 2, 3. ✓ Smoke project intentionally omitted (out of scope, deferred to plan 9).
   - Three test projects (Core / Api / Analyzers Tests) with xUnit + NSubstitute — Task 4. ✓
   - Analyzer `ADOMCP001` no-token-into-`ILogger` — Task 11. ✓
   - Analyzer `ADOMCP002` `[ExcludeFromCodeCoverage]` requires `Justification` — Task 12. ✓
   - Core abstractions per contracts doc (`IClock`, `IKeyVaultEncryptor`, `IEntraTokenClient` + `EntraTokenResult`, `ITokenStore` + `RegisteredClient`, `AuthorizationCodeRecord`, `TokenRecord`) — Tasks 5, 6, 7, 8. ✓
   - `SystemClock` implementation — Task 5. (Spec text mentions DI registration "in DI"; no DI container exists yet in this plan since `Api` is a skeleton. `SystemClock` is plain DI-ready; explicit registration deferred to Api wiring in later plans, but `SystemClock` itself is implemented and tested here per the brief.) ✓
   - In-memory `ITokenStore` at `tests/AdoMcpBridge.Core.Tests/InMemoryTokenStore.cs` with full coverage — Tasks 9, 10. ✓
   - Coverlet wired for `dotnet test /p:CollectCoverage=true /p:Threshold=100 /p:ThresholdType=line,branch,method` — Task 4 (`Directory.Build.targets` + package refs), Task 10/13 (verification commands). ✓

2. **Placeholder scan:** No `TBD`/`TODO`/"implement later"/"similar to Task N" patterns. Every code step contains literal C# or XML. Every run step has an exact command.

3. **Type & signature consistency:**
   - `EntraTokenResult(string, string, DateTimeOffset, string, string)` consistent across Task 6 definition and Task 6 tests.
   - `RegisteredClient`, `AuthorizationCodeRecord`, `TokenRecord` ctor positions match exactly between Task 7 definitions, Task 7 tests, Task 9/10 `InMemoryTokenStore` calls, and the contracts doc.
   - `ITokenStore` method signatures (`ValueTask<...>` async, names, parameter order) match the contracts doc exactly and the `InMemoryTokenStore` implementation in Task 9.
   - Diagnostic IDs `ADOMCP001` / `ADOMCP002` consistent across analyzer code, analyzer-tests, and `AnalyzerReleases.Unshipped.md` (Task 3).
   - Namespaces consistent: production lives under `AdoMcpBridge.Core.Abstractions` / `.Time`; analyzer code under `AdoMcpBridge.Analyzers`; tests under `AdoMcpBridge.Core.Tests.*` / `AdoMcpBridge.Analyzers.Tests`.

4. **Branching policy (CLAUDE.md):** Task 1 creates `claude/foundation-skeleton` off latest `main`; Task 14 pushes and opens a non-draft PR. No commits land on `main`. Conventional-Commits prefixes (`chore:`, `feat:`, `test:`) used throughout.

5. **TDD:** Every implementation task follows red → run-to-fail → green → run-to-pass → commit. Tasks 8 (interface) and 13 (project wiring) are pure scaffolding with no behavior, so they have no test step — acceptable because each behavior test added later (Tasks 9-12) implicitly proves the wiring works.
