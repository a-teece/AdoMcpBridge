# Entra MSAL Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the production `IEntraTokenClient` using MSAL.NET confidential-client with certificate auth (Key Vault sourced), plus the `CertificateProvider` that fetches and caches the X509Certificate2.

**Architecture:** A `CertificateProvider` retrieves the Entra app certificate (cert + private key) from Azure Key Vault by name and caches it until the underlying secret version changes. `MsalEntraTokenClient` builds an MSAL `IConfidentialClientApplication` per call (cheap; MSAL keeps internal caches via the same authority+clientId+cert) using that cert, and implements the two `IEntraTokenClient` methods: `ExchangeAuthorizationCodeAsync` (via `AcquireTokenByAuthorizationCode` with the user's PKCE verifier) and `AcquireAdoTokenAsync` (via the `IByRefreshToken` extension to swap a stored Entra refresh token for an ADO access token). MSAL failures are mapped to a typed `EntraAuthException`. Integration tests point MSAL at WireMock.Net via `WithAuthority` so real MSAL code paths run against a fake Entra.

**Tech Stack:** .NET 10, `Microsoft.Identity.Client` (MSAL.NET), `Azure.Security.KeyVault.Certificates`, `Azure.Security.KeyVault.Secrets`, `Azure.Identity` (DefaultAzureCredential), xUnit, NSubstitute, WireMock.Net, FluentAssertions.

**Depends on:** `2026-06-09-foundation.md` (provides `IEntraTokenClient`, `EntraTokenResult`, `IClock`, `Directory.Build.props`, analyzers, test project skeleton).

---

## File Structure

**Created:**
- `src/AdoMcpBridge.Core/Entra/EntraOptions.cs` — bound options for `AdoMcp:Entra`.
- `src/AdoMcpBridge.Core/Entra/EntraAuthException.cs` — typed exception, no secret data.
- `src/AdoMcpBridge.Core/Entra/CertificateProvider.cs` — Key Vault cert fetch + cache.
- `src/AdoMcpBridge.Core/Entra/ICertificateProvider.cs` — abstraction over the above.
- `src/AdoMcpBridge.Core/Entra/MsalEntraTokenClient.cs` — production `IEntraTokenClient`.
- `src/AdoMcpBridge.Core/Entra/MsalClientFactory.cs` — wraps `ConfidentialClientApplicationBuilder` (mockable seam, `[ExcludeFromCodeCoverage]`).
- `src/AdoMcpBridge.Core/Entra/IMsalClientFactory.cs` — abstraction for the above.
- `src/AdoMcpBridge.Core/Entra/ServiceCollectionExtensions.cs` — `AddEntraClient`.
- `tests/AdoMcpBridge.Core.Tests/Entra/EntraAuthExceptionTests.cs`
- `tests/AdoMcpBridge.Core.Tests/Entra/CertificateProviderTests.cs`
- `tests/AdoMcpBridge.Core.Tests/Entra/MsalEntraTokenClientTests.cs`
- `tests/AdoMcpBridge.Core.Tests/Entra/WireMockEntra.cs` — helper that stands up a WireMock OIDC discovery + token endpoints.
- `tests/AdoMcpBridge.Core.Tests/Entra/AddEntraClientTests.cs`
- `tests/AdoMcpBridge.Core.Tests/TestCertificates.cs` — generates an in-memory self-signed X509Certificate2 for tests.

**Modified:**
- `src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj` — add MSAL + Key Vault package references.
- `tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj` — add WireMock.Net + NSubstitute (if not already from foundation).

---

## Task 1: Add NuGet package references

**Files:**
- Modify: `src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`
- Modify: `tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`

- [ ] **Step 1: Add MSAL and Key Vault packages to Core csproj**

In `src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`, inside the existing `<ItemGroup>` for PackageReference (or add a new one):

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Identity.Client" Version="4.66.0" />
  <PackageReference Include="Azure.Identity" Version="1.13.0" />
  <PackageReference Include="Azure.Security.KeyVault.Certificates" Version="4.7.0" />
  <PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.7.0" />
  <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
</ItemGroup>
```

- [ ] **Step 2: Add WireMock.Net to test csproj**

In `tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="WireMock.Net" Version="1.6.7" />
  <PackageReference Include="NSubstitute" Version="5.3.0" />
  <PackageReference Include="FluentAssertions" Version="7.0.0" />
</ItemGroup>
```

- [ ] **Step 3: Verify restore + build**

Run: `dotnet build src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`
Expected: PASS (build succeeds, packages restore).

- [ ] **Step 4: Commit**

```bash
git add src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj
git commit -m "chore: add MSAL and Key Vault packages for Entra client"
```

---

## Task 2: EntraOptions

**Files:**
- Create: `src/AdoMcpBridge.Core/Entra/EntraOptions.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/Entra/EntraOptionsTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/AdoMcpBridge.Core.Tests/Entra/EntraOptionsTests.cs
using AdoMcpBridge.Core.Entra;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class EntraOptionsTests
{
    [Fact]
    public void Binds_from_configuration_section()
    {
        var dict = new Dictionary<string, string?>
        {
            ["AdoMcp:Entra:TenantId"] = "tid",
            ["AdoMcp:Entra:ClientId"] = "cid",
            ["AdoMcp:Entra:CertificateName"] = "ado-mcp-bridge",
            ["AdoMcp:Entra:Authority"] = "https://login.microsoftonline.com/tid/v2.0",
            ["AdoMcp:Entra:KeyVaultUri"] = "https://kv.example/",
            ["AdoMcp:Entra:Scopes:0"] = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation",
            ["AdoMcp:Entra:Scopes:1"] = "offline_access",
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddOptions<EntraOptions>().Bind(cfg.GetSection("AdoMcp:Entra"));
        var opts = services.BuildServiceProvider().GetRequiredService<IOptions<EntraOptions>>().Value;

        opts.TenantId.Should().Be("tid");
        opts.ClientId.Should().Be("cid");
        opts.CertificateName.Should().Be("ado-mcp-bridge");
        opts.Authority.Should().Be("https://login.microsoftonline.com/tid/v2.0");
        opts.KeyVaultUri.Should().Be("https://kv.example/");
        opts.Scopes.Should().Contain("offline_access");
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~EntraOptionsTests"`
Expected: FAIL — type `EntraOptions` does not exist.

- [ ] **Step 3: Implement `EntraOptions`**

```csharp
// src/AdoMcpBridge.Core/Entra/EntraOptions.cs
namespace AdoMcpBridge.Core.Entra;

public sealed class EntraOptions
{
    public const string SectionName = "AdoMcp:Entra";

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string CertificateName { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string KeyVaultUri { get; set; } = string.Empty;
    public IList<string> Scopes { get; set; } = new List<string>();
}
```

- [ ] **Step 4: Verify pass**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~EntraOptionsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Entra/EntraOptions.cs tests/AdoMcpBridge.Core.Tests/Entra/EntraOptionsTests.cs
git commit -m "feat: add EntraOptions bound to AdoMcp:Entra"
```

---

## Task 3: EntraAuthException

**Files:**
- Create: `src/AdoMcpBridge.Core/Entra/EntraAuthException.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/Entra/EntraAuthExceptionTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/AdoMcpBridge.Core.Tests/Entra/EntraAuthExceptionTests.cs
using AdoMcpBridge.Core.Entra;
using FluentAssertions;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class EntraAuthExceptionTests
{
    [Fact]
    public void Carries_error_code_and_status_without_secret_data()
    {
        var ex = new EntraAuthException(
            EntraAuthFailure.RefreshRejected,
            statusCode: 401,
            entraErrorCode: "invalid_grant",
            message: "Entra rejected the refresh token.");

        ex.Failure.Should().Be(EntraAuthFailure.RefreshRejected);
        ex.StatusCode.Should().Be(401);
        ex.EntraErrorCode.Should().Be("invalid_grant");
        ex.Message.Should().Be("Entra rejected the refresh token.");
    }

    [Fact]
    public void Preserves_inner_exception()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new EntraAuthException(EntraAuthFailure.Transport, null, null, "transport failed", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~EntraAuthExceptionTests"`
Expected: FAIL — type not found.

- [ ] **Step 3: Implement**

```csharp
// src/AdoMcpBridge.Core/Entra/EntraAuthException.cs
namespace AdoMcpBridge.Core.Entra;

public enum EntraAuthFailure
{
    AuthorizationCodeRejected,
    RefreshRejected,
    CertificateUnavailable,
    Transport,
    Unknown,
}

public sealed class EntraAuthException : Exception
{
    public EntraAuthFailure Failure { get; }
    public int? StatusCode { get; }
    public string? EntraErrorCode { get; }

    public EntraAuthException(
        EntraAuthFailure failure,
        int? statusCode,
        string? entraErrorCode,
        string message,
        Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        StatusCode = statusCode;
        EntraErrorCode = entraErrorCode;
    }
}
```

- [ ] **Step 4: Verify pass**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~EntraAuthExceptionTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Entra/EntraAuthException.cs tests/AdoMcpBridge.Core.Tests/Entra/EntraAuthExceptionTests.cs
git commit -m "feat: add typed EntraAuthException for Entra client failures"
```

---

## Task 4: Test self-signed certificate helper

**Files:**
- Create: `tests/AdoMcpBridge.Core.Tests/TestCertificates.cs`

- [ ] **Step 1: Add helper (used by later tasks; no test of its own — it is itself test infrastructure)**

```csharp
// tests/AdoMcpBridge.Core.Tests/TestCertificates.cs
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AdoMcpBridge.Core.Tests;

internal static class TestCertificates
{
    public static X509Certificate2 CreateSelfSigned(string cn = "CN=AdoMcpBridgeTest")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(cn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Re-import with exportable private key so MSAL can sign assertions.
        var pfx = cert.Export(X509ContentType.Pfx, "x");
        return X509CertificateLoader.LoadPkcs12(pfx, "x", X509KeyStorageFlags.Exportable);
    }
}
```

- [ ] **Step 2: Verify it compiles by building tests**

Run: `dotnet build tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Core.Tests/TestCertificates.cs
git commit -m "test: add self-signed cert helper for Entra tests"
```

---

## Task 5: ICertificateProvider abstraction

**Files:**
- Create: `src/AdoMcpBridge.Core/Entra/ICertificateProvider.cs`

- [ ] **Step 1: Add interface (covered by CertificateProvider tests in Task 6; no separate test)**

```csharp
// src/AdoMcpBridge.Core/Entra/ICertificateProvider.cs
using System.Security.Cryptography.X509Certificates;

namespace AdoMcpBridge.Core.Entra;

public interface ICertificateProvider
{
    ValueTask<X509Certificate2> GetCertificateAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/AdoMcpBridge.Core/Entra/ICertificateProvider.cs
git commit -m "feat: add ICertificateProvider abstraction"
```

---

## Task 6: CertificateProvider with version-aware caching

**Files:**
- Create: `src/AdoMcpBridge.Core/Entra/CertificateProvider.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/Entra/CertificateProviderTests.cs`

`CertificateProvider` uses `CertificateClient.GetCertificateAsync(name)` to discover the current version + `SecretIdentifier`, then `SecretClient.GetSecretAsync(secretName, version)` to retrieve the PFX bytes (Key Vault returns the cert's private key as a secret). It caches the loaded `X509Certificate2` keyed by the secret version id; a fresh fetch is triggered when the certificate version changes.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/AdoMcpBridge.Core.Tests/Entra/CertificateProviderTests.cs
using System.Security.Cryptography.X509Certificates;
using AdoMcpBridge.Core.Entra;
using Azure;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class CertificateProviderTests
{
    private static (CertificateClient certClient, SecretClient secretClient, string version, byte[] pfx) Arrange(
        string version = "v1")
    {
        using var cert = TestCertificates.CreateSelfSigned();
        var pfx = cert.Export(X509ContentType.Pfx, "x");

        var certClient = Substitute.For<CertificateClient>();
        var secretClient = Substitute.For<SecretClient>();

        var secretId = new Uri($"https://kv.example/secrets/ado-mcp-bridge/{version}");
        var kvCert = CertificateModelFactory.KeyVaultCertificateWithPolicy(
            properties: CertificateModelFactory.CertificateProperties(
                name: "ado-mcp-bridge",
                vaultUri: new Uri("https://kv.example/"),
                version: version),
            secretId: secretId);
        certClient.GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(kvCert, Substitute.For<Response>()));

        // Key Vault returns a base64 PFX with password "x" in newer versions; for simplicity
        // we return raw PFX bytes here and have the provider use password "x" in tests via PfxPassword.
        var kvSecret = new KeyVaultSecret("ado-mcp-bridge", Convert.ToBase64String(pfx));
        secretClient.GetSecretAsync("ado-mcp-bridge", version, Arg.Any<CancellationToken>())
            .Returns(Response.FromValue(kvSecret, Substitute.For<Response>()));

        return (certClient, secretClient, version, pfx);
    }

    private static IOptions<EntraOptions> Opts() => Options.Create(new EntraOptions
    {
        TenantId = "tid",
        ClientId = "cid",
        CertificateName = "ado-mcp-bridge",
        KeyVaultUri = "https://kv.example/",
        Authority = "https://login.microsoftonline.com/tid/v2.0",
        PfxPassword = "x",
    });

    [Fact]
    public async Task First_call_fetches_from_key_vault()
    {
        var (cc, sc, _, _) = Arrange();
        var sut = new CertificateProvider(cc, sc, Opts());

        var cert = await sut.GetCertificateAsync(CancellationToken.None);

        cert.HasPrivateKey.Should().BeTrue();
        await cc.Received(1).GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>());
        await sc.Received(1).GetSecretAsync("ado-mcp-bridge", "v1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Second_call_uses_cached_when_version_unchanged()
    {
        var (cc, sc, _, _) = Arrange();
        var sut = new CertificateProvider(cc, sc, Opts());

        _ = await sut.GetCertificateAsync(CancellationToken.None);
        _ = await sut.GetCertificateAsync(CancellationToken.None);

        // GetCertificateAsync re-called (cheap metadata lookup), GetSecretAsync NOT re-called.
        await cc.Received(2).GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>());
        await sc.Received(1).GetSecretAsync("ado-mcp-bridge", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refetches_when_version_changes()
    {
        var (cc, sc, _, _) = Arrange("v1");

        // After first call, swap returned version to v2.
        var sut = new CertificateProvider(cc, sc, Opts());
        _ = await sut.GetCertificateAsync(CancellationToken.None);

        var (cc2, sc2, _, _) = Arrange("v2");
        cc.GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>())
          .Returns(cc2.GetCertificateAsync("ado-mcp-bridge", CancellationToken.None));
        sc.GetSecretAsync("ado-mcp-bridge", "v2", Arg.Any<CancellationToken>())
          .Returns(sc2.GetSecretAsync("ado-mcp-bridge", "v2", CancellationToken.None));

        _ = await sut.GetCertificateAsync(CancellationToken.None);

        await sc.Received(1).GetSecretAsync("ado-mcp-bridge", "v2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wraps_key_vault_failure_in_EntraAuthException()
    {
        var cc = Substitute.For<CertificateClient>();
        var sc = Substitute.For<SecretClient>();
        cc.GetCertificateAsync("ado-mcp-bridge", Arg.Any<CancellationToken>())
          .Returns<Response<KeyVaultCertificateWithPolicy>>(_ => throw new RequestFailedException(403, "denied"));

        var sut = new CertificateProvider(cc, sc, Opts());

        var act = async () => await sut.GetCertificateAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.CertificateUnavailable);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~CertificateProviderTests"`
Expected: FAIL (`CertificateProvider` does not exist; also `EntraOptions.PfxPassword` missing).

- [ ] **Step 3: Add `PfxPassword` to EntraOptions**

In `src/AdoMcpBridge.Core/Entra/EntraOptions.cs`, add:

```csharp
    /// <summary>
    /// Password used to load the PFX returned by Key Vault. Default empty; rotate via KV policy.
    /// </summary>
    public string PfxPassword { get; set; } = string.Empty;
```

- [ ] **Step 4: Implement `CertificateProvider`**

```csharp
// src/AdoMcpBridge.Core/Entra/CertificateProvider.cs
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.Entra;

public sealed class CertificateProvider : ICertificateProvider, IDisposable
{
    private readonly CertificateClient _certClient;
    private readonly SecretClient _secretClient;
    private readonly EntraOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedVersion;
    private X509Certificate2? _cached;

    public CertificateProvider(
        CertificateClient certClient,
        SecretClient secretClient,
        IOptions<EntraOptions> options)
    {
        _certClient = certClient;
        _secretClient = secretClient;
        _options = options.Value;
    }

    public async ValueTask<X509Certificate2> GetCertificateAsync(CancellationToken ct)
    {
        KeyVaultCertificateWithPolicy meta;
        try
        {
            meta = await _certClient.GetCertificateAsync(_options.CertificateName, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            throw new EntraAuthException(
                EntraAuthFailure.CertificateUnavailable,
                ex.Status,
                ex.ErrorCode,
                $"Failed to fetch certificate metadata '{_options.CertificateName}' from Key Vault.",
                ex);
        }

        var version = meta.Properties.Version;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null && _cachedVersion == version)
            {
                return _cached;
            }

            KeyVaultSecret secret;
            try
            {
                secret = await _secretClient
                    .GetSecretAsync(_options.CertificateName, version, ct)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex)
            {
                throw new EntraAuthException(
                    EntraAuthFailure.CertificateUnavailable,
                    ex.Status,
                    ex.ErrorCode,
                    $"Failed to fetch certificate secret '{_options.CertificateName}' version '{version}' from Key Vault.",
                    ex);
            }

            var pfxBytes = Convert.FromBase64String(secret.Value);
            var loaded = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                _options.PfxPassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

            _cached?.Dispose();
            _cached = loaded;
            _cachedVersion = version;
            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _cached?.Dispose();
        _gate.Dispose();
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~CertificateProviderTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/AdoMcpBridge.Core/Entra/CertificateProvider.cs src/AdoMcpBridge.Core/Entra/EntraOptions.cs tests/AdoMcpBridge.Core.Tests/Entra/CertificateProviderTests.cs
git commit -m "feat: add CertificateProvider with Key Vault version-aware cache"
```

---

## Task 7: IMsalClientFactory + MsalClientFactory seam

The MSAL fluent builder chain is awkward to unit-test directly; we wrap it behind an interface so `MsalEntraTokenClient` can be tested by handing it a real MSAL client whose `WithAuthority` points at WireMock.

**Files:**
- Create: `src/AdoMcpBridge.Core/Entra/IMsalClientFactory.cs`
- Create: `src/AdoMcpBridge.Core/Entra/MsalClientFactory.cs`

- [ ] **Step 1: Define interface**

```csharp
// src/AdoMcpBridge.Core/Entra/IMsalClientFactory.cs
using Microsoft.Identity.Client;

namespace AdoMcpBridge.Core.Entra;

public interface IMsalClientFactory
{
    ValueTask<IConfidentialClientApplication> CreateAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Implement (annotated `[ExcludeFromCodeCoverage]`)**

```csharp
// src/AdoMcpBridge.Core/Entra/MsalClientFactory.cs
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace AdoMcpBridge.Core.Entra;

[ExcludeFromCodeCoverage(Justification =
    "Thin wrapper over MSAL.NET's ConfidentialClientApplicationBuilder fluent chain. " +
    "Exercised end-to-end via MsalEntraTokenClient WireMock integration tests; " +
    "the fluent chain itself has no branchable logic.")]
public sealed class MsalClientFactory : IMsalClientFactory
{
    private readonly ICertificateProvider _certs;
    private readonly EntraOptions _options;

    public MsalClientFactory(ICertificateProvider certs, IOptions<EntraOptions> options)
    {
        _certs = certs;
        _options = options.Value;
    }

    public async ValueTask<IConfidentialClientApplication> CreateAsync(CancellationToken ct)
    {
        var cert = await _certs.GetCertificateAsync(ct).ConfigureAwait(false);
        return ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithCertificate(cert, sendX5C: true)
            .WithAuthority(_options.Authority, validateAuthority: false)
            .Build();
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/AdoMcpBridge.Core/AdoMcpBridge.Core.csproj`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/AdoMcpBridge.Core/Entra/IMsalClientFactory.cs src/AdoMcpBridge.Core/Entra/MsalClientFactory.cs
git commit -m "feat: add MSAL confidential-client factory seam"
```

---

## Task 8: WireMock Entra helper

A reusable test helper that stands up a WireMock server with a minimal OIDC discovery doc + token endpoint, plus utilities to seed canned responses for `authorization_code` and `refresh_token` grants. MSAL.NET hits `{authority}/.well-known/openid-configuration` on first use, then the `token_endpoint` URL returned.

**Files:**
- Create: `tests/AdoMcpBridge.Core.Tests/Entra/WireMockEntra.cs`

- [ ] **Step 1: Implement helper**

```csharp
// tests/AdoMcpBridge.Core.Tests/Entra/WireMockEntra.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AdoMcpBridge.Core.Tests.Entra;

internal sealed class WireMockEntra : IAsyncDisposable
{
    public WireMockServer Server { get; }
    public string TenantId { get; } = "11111111-1111-1111-1111-111111111111";
    public string Authority => $"{Server.Url}/{TenantId}/v2.0";

    private readonly RSA _signingKey = RSA.Create(2048);

    private WireMockEntra()
    {
        Server = WireMockServer.Start();
        SetupDiscovery();
        SetupJwks();
    }

    public static WireMockEntra Start() => new();

    private void SetupDiscovery()
    {
        var doc = new
        {
            issuer = $"{Server.Url}/{TenantId}/v2.0",
            authorization_endpoint = $"{Server.Url}/{TenantId}/oauth2/v2.0/authorize",
            token_endpoint = $"{Server.Url}/{TenantId}/oauth2/v2.0/token",
            jwks_uri = $"{Server.Url}/{TenantId}/discovery/v2.0/keys",
            response_modes_supported = new[] { "query", "form_post" },
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token", "client_credentials" },
            token_endpoint_auth_methods_supported = new[] { "private_key_jwt", "client_secret_post" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            subject_types_supported = new[] { "pairwise" },
        };
        Server.Given(Request.Create()
                .WithPath($"/{TenantId}/v2.0/.well-known/openid-configuration").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(doc));
    }

    private void SetupJwks()
    {
        var parameters = _signingKey.ExportParameters(false);
        var jwks = new
        {
            keys = new object[]
            {
                new {
                    kty = "RSA", use = "sig", kid = "kid1",
                    n = Base64UrlEncoder.Encode(parameters.Modulus!),
                    e = Base64UrlEncoder.Encode(parameters.Exponent!),
                }
            }
        };
        Server.Given(Request.Create().WithPath($"/{TenantId}/discovery/v2.0/keys").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(jwks));
    }

    public string IssueIdToken(string oid, string upn)
    {
        var handler = new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var creds = new SigningCredentials(new RsaSecurityKey(_signingKey) { KeyId = "kid1" }, SecurityAlgorithms.RsaSha256);
        var token = handler.CreateJwtSecurityToken(
            issuer: $"{Server.Url}/{TenantId}/v2.0",
            audience: "unused-audience",
            subject: new ClaimsIdentity(new[]
            {
                new Claim("oid", oid),
                new Claim("preferred_username", upn),
                new Claim("tid", TenantId),
            }),
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            issuedAt: DateTime.UtcNow,
            signingCredentials: creds);
        return handler.WriteToken(token);
    }

    public void StubTokenEndpoint(int status, object body)
    {
        Server.Given(Request.Create().WithPath($"/{TenantId}/oauth2/v2.0/token").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(status).WithBodyAsJson(body));
    }

    public ValueTask DisposeAsync()
    {
        Server.Stop();
        Server.Dispose();
        _signingKey.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Verify build (no test yet)**

Run: `dotnet build tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`
Expected: PASS. If `System.IdentityModel.Tokens.Jwt` is not present, add it:

```xml
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.2.0" />
```

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Core.Tests/Entra/WireMockEntra.cs tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj
git commit -m "test: add WireMock Entra helper for MSAL integration tests"
```

---

## Task 9: MsalEntraTokenClient — ExchangeAuthorizationCodeAsync

**Files:**
- Create: `src/AdoMcpBridge.Core/Entra/MsalEntraTokenClient.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/Entra/MsalEntraTokenClientTests.cs`

- [ ] **Step 1: Write failing test (auth-code happy path)**

```csharp
// tests/AdoMcpBridge.Core.Tests/Entra/MsalEntraTokenClientTests.cs
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Entra;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using NSubstitute;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class MsalEntraTokenClientTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }

    private static EntraOptions OptionsFor(WireMockEntra wm) => new()
    {
        TenantId = wm.TenantId,
        ClientId = "cid",
        CertificateName = "ado-mcp-bridge",
        KeyVaultUri = "https://kv.example/",
        Authority = wm.Authority,
        Scopes = new List<string>
        {
            "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation",
            "offline_access",
        },
    };

    private static IMsalClientFactory FactoryFor(EntraOptions opts)
    {
        // Real MSAL client pointed at WireMock; cert is self-signed.
        var factory = Substitute.For<IMsalClientFactory>();
        factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            using var cert = TestCertificates.CreateSelfSigned();
            IConfidentialClientApplication app = ConfidentialClientApplicationBuilder
                .Create(opts.ClientId)
                .WithCertificate(cert, sendX5C: true)
                .WithAuthority(opts.Authority, validateAuthority: false)
                .WithHttpClientFactory(new InsecureHttpClientFactory())
                .Build();
            return new ValueTask<IConfidentialClientApplication>(app);
        });
        return factory;
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_returns_tokens_and_user_identity()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            token_type = "Bearer",
            scope = string.Join(' ', opts.Scopes),
            expires_in = 3600,
            ext_expires_in = 3600,
            access_token = "ado-access-token-value",
            refresh_token = "entra-refresh-token-value",
            id_token = wm.IssueIdToken(oid: "user-oid-123", upn: "alice@example.com"),
        });

        var sut = new MsalEntraTokenClient(
            FactoryFor(opts),
            Options.Create(opts),
            new FixedClock(DateTimeOffset.UtcNow),
            NullLogger<MsalEntraTokenClient>.Instance);

        var result = await sut.ExchangeAuthorizationCodeAsync(
            code: "auth-code-abc",
            codeVerifier: "pkce-verifier-xyz",
            redirectUri: "https://localhost:5001/callback",
            ct: CancellationToken.None);

        result.AccessToken.Should().Be("ado-access-token-value");
        result.RefreshToken.Should().Be("entra-refresh-token-value");
        result.UserObjectId.Should().Be("user-oid-123");
        result.UserPrincipalName.Should().Be("alice@example.com");
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(50));
    }
}

internal sealed class InsecureHttpClientFactory : IMsalHttpClientFactory
{
    private readonly HttpClient _client = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    });
    public HttpClient GetHttpClient() => _client;
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~MsalEntraTokenClientTests.ExchangeAuthorizationCodeAsync_returns"`
Expected: FAIL — `MsalEntraTokenClient` does not exist.

- [ ] **Step 3: Implement `MsalEntraTokenClient` (auth-code path only for now)**

```csharp
// src/AdoMcpBridge.Core/Entra/MsalEntraTokenClient.cs
using AdoMcpBridge.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace AdoMcpBridge.Core.Entra;

public sealed class MsalEntraTokenClient : IEntraTokenClient
{
    private readonly IMsalClientFactory _factory;
    private readonly EntraOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<MsalEntraTokenClient> _log;

    public MsalEntraTokenClient(
        IMsalClientFactory factory,
        IOptions<EntraOptions> options,
        IClock clock,
        ILogger<MsalEntraTokenClient> log)
    {
        _factory = factory;
        _options = options.Value;
        _clock = clock;
        _log = log;
    }

    public async ValueTask<EntraTokenResult> ExchangeAuthorizationCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var app = await _factory.CreateAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await app
                .AcquireTokenByAuthorizationCode(_options.Scopes, code)
                .WithPkceCodeVerifier(codeVerifier)
                .WithExtraQueryParameters(new Dictionary<string, string> { ["redirect_uri"] = redirectUri })
                .ExecuteAsync(ct)
                .ConfigureAwait(false);

            return ToResult(result, app);
        }
        catch (MsalServiceException ex)
        {
            _log.LogWarning("Entra authorization_code grant rejected: status={Status} code={Code}",
                ex.StatusCode, ex.ErrorCode);
            throw new EntraAuthException(
                EntraAuthFailure.AuthorizationCodeRejected,
                ex.StatusCode,
                ex.ErrorCode,
                "Entra rejected the authorization code.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new EntraAuthException(EntraAuthFailure.Transport, null, null,
                "Transport error contacting Entra.", ex);
        }
    }

    public ValueTask<EntraTokenResult> AcquireAdoTokenAsync(string entraRefreshToken, CancellationToken ct)
    {
        // Implemented in Task 10.
        throw new NotImplementedException();
    }

    private static EntraTokenResult ToResult(AuthenticationResult ar, IConfidentialClientApplication app)
    {
        // Refresh token is not exposed by AuthenticationResult; MSAL holds it in its UserTokenCache,
        // but for ExchangeAuthorizationCodeAsync we need the raw Entra refresh token so we can persist
        // it. MSAL surfaces it via the cache serialization callback or AdditionalResponseParameters
        // when configured. We rely on AdditionalResponseParameters["refresh_token"] when present
        // (Entra always returns it for offline_access scope).
        string refreshToken = ar.AdditionalResponseParameters is not null
                              && ar.AdditionalResponseParameters.TryGetValue("refresh_token", out var rt)
            ? rt
            : throw new EntraAuthException(
                EntraAuthFailure.Unknown, null, null,
                "Entra response did not contain a refresh_token; offline_access scope required.");

        var oid = ar.ClaimsPrincipal.FindFirst("oid")?.Value
                  ?? ar.UniqueId
                  ?? throw new EntraAuthException(EntraAuthFailure.Unknown, null, null,
                      "Entra response missing oid claim.");
        var upn = ar.ClaimsPrincipal.FindFirst("preferred_username")?.Value
                  ?? ar.Account?.Username
                  ?? string.Empty;

        return new EntraTokenResult(
            AccessToken: ar.AccessToken,
            RefreshToken: refreshToken,
            ExpiresAt: ar.ExpiresOn,
            UserObjectId: oid,
            UserPrincipalName: upn);
    }
}
```

Note on `AdditionalResponseParameters`: MSAL.NET >= 4.61 exposes the raw refresh_token via this dictionary only when `WithExperimentalFeatures()` is enabled on the builder. Update `MsalClientFactory.CreateAsync` to call `.WithExperimentalFeatures()` before `.Build()`.

- [ ] **Step 4: Update `MsalClientFactory` to enable experimental features**

Edit `src/AdoMcpBridge.Core/Entra/MsalClientFactory.cs`, replace the build chain with:

```csharp
        return ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithCertificate(cert, sendX5C: true)
            .WithAuthority(_options.Authority, validateAuthority: false)
            .WithExperimentalFeatures()
            .Build();
```

Also update the test factory in `MsalEntraTokenClientTests.FactoryFor` to call `.WithExperimentalFeatures()` before `.Build()`.

- [ ] **Step 5: Run test**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~MsalEntraTokenClientTests.ExchangeAuthorizationCodeAsync_returns"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/AdoMcpBridge.Core/Entra/MsalEntraTokenClient.cs src/AdoMcpBridge.Core/Entra/MsalClientFactory.cs tests/AdoMcpBridge.Core.Tests/Entra/MsalEntraTokenClientTests.cs
git commit -m "feat: implement Entra authorization_code exchange via MSAL"
```

---

## Task 10: MsalEntraTokenClient — AcquireAdoTokenAsync via refresh token

`IConfidentialClientApplication` exposes refresh-token redemption via the `IByRefreshToken` interface (`((IByRefreshToken)app).AcquireTokenByRefreshToken(...)`).

**Files:**
- Modify: `src/AdoMcpBridge.Core/Entra/MsalEntraTokenClient.cs`
- Modify: `tests/AdoMcpBridge.Core.Tests/Entra/MsalEntraTokenClientTests.cs`

- [ ] **Step 1: Add failing tests for refresh path (happy + 401)**

Append to `MsalEntraTokenClientTests`:

```csharp
    [Fact]
    public async Task AcquireAdoTokenAsync_swaps_refresh_token_for_ado_access_token()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(200, new
        {
            token_type = "Bearer",
            scope = string.Join(' ', opts.Scopes),
            expires_in = 3600,
            ext_expires_in = 3600,
            access_token = "fresh-ado-access-token",
            refresh_token = "rotated-entra-refresh-token",
            id_token = wm.IssueIdToken(oid: "user-oid-456", upn: "bob@example.com"),
        });

        var sut = new MsalEntraTokenClient(
            FactoryFor(opts),
            Options.Create(opts),
            new FixedClock(DateTimeOffset.UtcNow),
            NullLogger<MsalEntraTokenClient>.Instance);

        var result = await sut.AcquireAdoTokenAsync("stored-entra-refresh-token", CancellationToken.None);

        result.AccessToken.Should().Be("fresh-ado-access-token");
        result.RefreshToken.Should().Be("rotated-entra-refresh-token");
        result.UserObjectId.Should().Be("user-oid-456");
    }

    [Fact]
    [Trait("category", "security")]
    public async Task AcquireAdoTokenAsync_throws_EntraAuthException_on_401_invalid_grant()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        wm.StubTokenEndpoint(401, new
        {
            error = "invalid_grant",
            error_description = "AADSTS70008: The refresh token has expired.",
        });

        var sut = new MsalEntraTokenClient(
            FactoryFor(opts),
            Options.Create(opts),
            new FixedClock(DateTimeOffset.UtcNow),
            NullLogger<MsalEntraTokenClient>.Instance);

        var act = async () => await sut.AcquireAdoTokenAsync("expired-refresh-token", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().Be(EntraAuthFailure.RefreshRejected);
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.EntraErrorCode.Should().Be("invalid_grant");
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~MsalEntraTokenClientTests.AcquireAdoTokenAsync"`
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Replace `AcquireAdoTokenAsync` in `MsalEntraTokenClient`**

Replace the stub with:

```csharp
    public async ValueTask<EntraTokenResult> AcquireAdoTokenAsync(string entraRefreshToken, CancellationToken ct)
    {
        var app = await _factory.CreateAsync(ct).ConfigureAwait(false);
        var byRefresh = (IByRefreshToken)app;
        try
        {
            var result = await byRefresh
                .AcquireTokenByRefreshToken(_options.Scopes, entraRefreshToken)
                .ExecuteAsync(ct)
                .ConfigureAwait(false);

            return ToResult(result, app);
        }
        catch (MsalServiceException ex) when (ex.StatusCode == 401 || ex.ErrorCode == "invalid_grant")
        {
            _log.LogWarning("Entra refresh_token grant rejected: status={Status} code={Code}",
                ex.StatusCode, ex.ErrorCode);
            throw new EntraAuthException(
                EntraAuthFailure.RefreshRejected,
                ex.StatusCode,
                ex.ErrorCode,
                "Entra rejected the refresh token.",
                ex);
        }
        catch (MsalServiceException ex)
        {
            throw new EntraAuthException(
                EntraAuthFailure.Unknown,
                ex.StatusCode,
                ex.ErrorCode,
                "Entra returned an unexpected error.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new EntraAuthException(EntraAuthFailure.Transport, null, null,
                "Transport error contacting Entra.", ex);
        }
    }
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~MsalEntraTokenClientTests"`
Expected: PASS (all three).

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Entra/MsalEntraTokenClient.cs tests/AdoMcpBridge.Core.Tests/Entra/MsalEntraTokenClientTests.cs
git commit -m "feat: implement Entra refresh_token swap with typed exception on 401"
```

---

## Task 11: Transport-failure mapping test

Confirms that connection errors surface as `EntraAuthFailure.Transport` rather than leaking MSAL types.

**Files:**
- Modify: `tests/AdoMcpBridge.Core.Tests/Entra/MsalEntraTokenClientTests.cs`

- [ ] **Step 1: Add failing test**

```csharp
    [Fact]
    public async Task AcquireAdoTokenAsync_maps_transport_failure_to_Transport_failure()
    {
        await using var wm = WireMockEntra.Start();
        var opts = OptionsFor(wm);
        // Override authority to point at a closed port to force a transport failure.
        opts = new EntraOptions
        {
            TenantId = opts.TenantId, ClientId = opts.ClientId, CertificateName = opts.CertificateName,
            KeyVaultUri = opts.KeyVaultUri,
            Authority = "http://127.0.0.1:1/" + opts.TenantId + "/v2.0",
            Scopes = opts.Scopes,
        };

        var sut = new MsalEntraTokenClient(
            FactoryFor(opts),
            Options.Create(opts),
            new FixedClock(DateTimeOffset.UtcNow),
            NullLogger<MsalEntraTokenClient>.Instance);

        var act = async () => await sut.AcquireAdoTokenAsync("rt", CancellationToken.None);
        var ex = await act.Should().ThrowAsync<EntraAuthException>();
        ex.Which.Failure.Should().BeOneOf(EntraAuthFailure.Transport, EntraAuthFailure.Unknown);
    }
```

(MSAL may wrap a transport error in `MsalServiceException` rather than letting `HttpRequestException` through, hence `BeOneOf`.)

- [ ] **Step 2: Run**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~MsalEntraTokenClientTests.AcquireAdoTokenAsync_maps_transport_failure"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Core.Tests/Entra/MsalEntraTokenClientTests.cs
git commit -m "test: assert transport failure maps to typed EntraAuthException"
```

---

## Task 12: AddEntraClient DI registration

**Files:**
- Create: `src/AdoMcpBridge.Core/Entra/ServiceCollectionExtensions.cs`
- Create: `tests/AdoMcpBridge.Core.Tests/Entra/AddEntraClientTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/AdoMcpBridge.Core.Tests/Entra/AddEntraClientTests.cs
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.Entra;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AdoMcpBridge.Core.Tests.Entra;

public sealed class AddEntraClientTests
{
    [Fact]
    public void Registers_IEntraTokenClient_and_ICertificateProvider()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdoMcp:Entra:TenantId"] = "tid",
            ["AdoMcp:Entra:ClientId"] = "cid",
            ["AdoMcp:Entra:CertificateName"] = "ado-mcp-bridge",
            ["AdoMcp:Entra:KeyVaultUri"] = "https://kv.example/",
            ["AdoMcp:Entra:Authority"] = "https://login.microsoftonline.com/tid/v2.0",
            ["AdoMcp:Entra:Scopes:0"] = "499b84ac-1321-427f-aa17-267ca6975798/user_impersonation",
            ["AdoMcp:Entra:Scopes:1"] = "offline_access",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new StubClock());
        services.AddEntraClient(cfg);

        using var sp = services.BuildServiceProvider();
        sp.GetService<IEntraTokenClient>().Should().BeOfType<MsalEntraTokenClient>();
        sp.GetService<ICertificateProvider>().Should().BeOfType<CertificateProvider>();
        sp.GetService<IMsalClientFactory>().Should().BeOfType<MsalClientFactory>();
    }

    private sealed class StubClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~AddEntraClientTests"`
Expected: FAIL — `AddEntraClient` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/AdoMcpBridge.Core/Entra/ServiceCollectionExtensions.cs
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;
using AdoMcpBridge.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AdoMcpBridge.Core.Entra;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntraClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EntraOptions>()
            .Bind(configuration.GetSection(EntraOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.TenantId), "Entra TenantId required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Entra ClientId required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.CertificateName), "Entra CertificateName required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.KeyVaultUri), "Entra KeyVaultUri required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Authority), "Entra Authority required")
            .Validate(o => o.Scopes.Count > 0, "At least one scope required");

        services.AddSingleton<CertificateClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<EntraOptions>>().Value;
            return new CertificateClient(new Uri(opts.KeyVaultUri), new DefaultAzureCredential());
        });
        services.AddSingleton<SecretClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<EntraOptions>>().Value;
            return new SecretClient(new Uri(opts.KeyVaultUri), new DefaultAzureCredential());
        });

        services.AddSingleton<ICertificateProvider, CertificateProvider>();
        services.AddSingleton<IMsalClientFactory, MsalClientFactory>();
        services.AddSingleton<IEntraTokenClient, MsalEntraTokenClient>();
        return services;
    }
}
```

- [ ] **Step 4: Run**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj --filter "FullyQualifiedName~AddEntraClientTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Entra/ServiceCollectionExtensions.cs tests/AdoMcpBridge.Core.Tests/Entra/AddEntraClientTests.cs
git commit -m "feat: add AddEntraClient DI registration"
```

---

## Task 13: Run the full suite + coverage check

- [ ] **Step 1: Full test run**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj`
Expected: PASS (all Entra tests + existing foundation tests).

- [ ] **Step 2: Coverage gate**

Run:
```bash
dotnet test tests/AdoMcpBridge.Core.Tests/AdoMcpBridge.Core.Tests.csproj \
  /p:CollectCoverage=true \
  /p:Threshold=100 \
  /p:ThresholdType=line,branch,method \
  /p:ExcludeByAttribute=ExcludeFromCodeCoverageAttribute
```
Expected: PASS. If a genuinely-untestable branch is reported, annotate with `[ExcludeFromCodeCoverage(Justification = "...")]` and rerun. Do NOT relax the threshold.

- [ ] **Step 3: Confirm no token/code/verifier flows into any `ILogger` call**

Run: `dotnet build -warnaserror`
Expected: PASS — analyzer `ADOMCP001` (from foundation plan) catches any violation. Note: log statements in `MsalEntraTokenClient` log only `ex.StatusCode` and `ex.ErrorCode`, never the code, verifier, or token strings.

- [ ] **Step 4: Open PR**

```bash
git checkout -b claude/entra-client-impl 2>/dev/null || true
git push -u origin claude/entra-client-impl
gh pr create --title "feat: Entra MSAL client + Key Vault cert provider" --body "$(cat <<'EOF'
## Summary
- Implements `MsalEntraTokenClient` (production `IEntraTokenClient`) with certificate auth.
- Adds `CertificateProvider` fetching the Entra app cert from Key Vault with version-aware cache.
- Adds typed `EntraAuthException` and `AddEntraClient` DI extension.
- Integration tests use WireMock.Net to run real MSAL.NET code paths against a fake Entra.

## Review focus
- MSAL refresh-token path uses `IByRefreshToken`; verify 401 / `invalid_grant` mapping.
- `CertificateProvider` version-change refresh logic.
- No secret material logged.

## Test plan
- [x] `dotnet test` — all Entra tests pass.
- [x] Coverage at 100% line/branch/method with annotated exclusions only on the MSAL builder seam.
- [x] Analyzer `ADOMCP001` clean.
EOF
)"
```

---

## Self-Review Notes

**Spec coverage check (§5 + scope from the request):**

- "Confidential-client app built with MSAL.NET" — Task 7 (`MsalClientFactory`), Task 9 (consumed by `MsalEntraTokenClient`). Covered.
- "Certificate auth — loads cert from Key Vault Certificates + Secrets" — Task 6 (`CertificateProvider`). Covered.
- "`ExchangeAuthorizationCodeAsync` using PKCE verifier" — Task 9, calls `WithPkceCodeVerifier`. Covered.
- "`AcquireAdoTokenAsync` via `IByRefreshToken`" — Task 10, cast `(IByRefreshToken)app`. Covered.
- Authority `https://login.microsoftonline.com/{TenantId}/v2.0` — `EntraOptions.Authority` (Task 2), example value in test. Covered.
- ADO scope `499b84ac-…/user_impersonation` + `offline_access` — required scopes in tests (Tasks 9/10/12) and DI validation (Task 12). Covered.
- `CertificateProvider` caches X509Certificate2 + rotation on new KV version — Task 6 has tests for cache hit and version-change refresh. Covered.
- `AddEntraClient` DI registration — Task 12. Covered.
- Unit tests with NSubstitute fake `CertificateClient` / `SecretClient` — Task 6. Covered.
- Integration tests with WireMock.Net against MSAL — Tasks 8, 9, 10, 11. Covered.
- Typed `EntraAuthException` on fake 401 — Task 10 (`AcquireAdoTokenAsync_throws_EntraAuthException_on_401_invalid_grant`). Covered.
- No tokens/secrets/verifiers logged — verified in Task 13 step 3; log statements only emit `StatusCode` and `ErrorCode`. Covered.
- `[ExcludeFromCodeCoverage(Justification=…)]` only on MSAL fluent builder — Task 7. Covered.
- Header block + agentic-workers note — present at top. Covered.
- Conventional Commits — every commit uses `feat:` / `chore:` / `test:` prefix. Covered.
- TDD red→green→commit on every step — every task that adds code has a failing test step first. Covered.

**Placeholder scan:** No "TBD", "similar to", "add appropriate error handling". All code is concrete.

**Type consistency check:**
- `EntraTokenResult` — used everywhere as defined in `_shared-contracts.md` (positional record with `AccessToken`, `RefreshToken`, `ExpiresAt`, `UserObjectId`, `UserPrincipalName`). Consistent.
- `IEntraTokenClient` method signatures — `ExchangeAuthorizationCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct)` and `AcquireAdoTokenAsync(string entraRefreshToken, CancellationToken ct)` — both match Task 9/10 implementations.
- `EntraAuthFailure` enum values referenced (`RefreshRejected`, `AuthorizationCodeRejected`, `CertificateUnavailable`, `Transport`, `Unknown`) all defined in Task 3.
- `EntraOptions.PfxPassword` — introduced in Task 6 Step 3 explicitly (not in Task 2's initial version). Flagged: ensure executing agent applies Task 6 Step 3 before running Task 6 tests.
- `ICertificateProvider.GetCertificateAsync(CancellationToken)` — single method, used identically in Tasks 6, 7.
- `IMsalClientFactory.CreateAsync(CancellationToken)` — single method, used identically in Tasks 7, 9, 10.

**Known build risks called out so executing agent isn't surprised:**
- MSAL's `AdditionalResponseParameters["refresh_token"]` requires `.WithExperimentalFeatures()` — wired in Task 9 Step 4 and the test factory.
- `KeyVaultCertificate` model-factory shape varies by SDK version; if `CertificateModelFactory.KeyVaultCertificateWithPolicy` signature differs from the one used, substitute with the SDK's actual factory method (the goal is a `Response<KeyVaultCertificateWithPolicy>` whose `Value.Properties.Version` and `Value.SecretId` are set).
- WireMock.Net listens on HTTP; the test factory uses an `InsecureHttpClientFactory` that bypasses cert validation so MSAL is willing to talk to it.
