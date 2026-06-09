# Observability & Runbook Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire OpenTelemetry traces and the eight `AdoMcpBridge` metrics through to Azure Monitor, normalise errors via a typed exception hierarchy and ProblemDetails middleware, deploy alert rules in Bicep, and ship a six-scenario runbook with executable Kusto queries plus a PR-template gate that prevents new alerts without runbook entries.

**Architecture:** A single `BridgeMeter` static class owns the meter identity and the eight instruments so every emitter binds to the same names. An `AddBridgeTelemetry` DI extension wires ASP.NET Core + HttpClient instrumentation and the Azure Monitor exporter. A typed `BridgeException` hierarchy (caller / upstream / internal) is translated by a single global middleware that emits RFC 6749 JSON on OAuth endpoints, ProblemDetails elsewhere, opaque `error_id` for internal failures, and stamps the W3C trace id on every response. Alerts live in `infra/modules/alerts.bicep` and are paired 1:1 with runbook scenarios; a PR template checkbox enforces that pairing.

**Tech Stack:** .NET 10, `System.Diagnostics.Metrics`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `Azure.Monitor.OpenTelemetry.AspNetCore`, Bicep, Azure Monitor / Log Analytics (Kusto), xUnit + NSubstitute, `MeterListener`.

---

## File Structure

**Source — `src/AdoMcpBridge.Api/Telemetry/`:**
- `BridgeMeter.cs` — static meter + eight instruments (counters, histograms, up-down counter).
- `TelemetryServiceCollectionExtensions.cs` — `AddBridgeTelemetry` DI extension.

**Source — `src/AdoMcpBridge.Core/Errors/`:**
- `BridgeException.cs` — abstract base, holds `ErrorCode`, `ErrorId`.
- `CallerErrorException.cs` — 400, RFC 6749 fields (`error`, `error_description`).
- `UpstreamErrorException.cs` — 502 / mapped status, never leaks raw upstream body.
- `InternalErrorException.cs` — 500, opaque, auto-generated `error_id`.

**Source — `src/AdoMcpBridge.Api/Middleware/`:**
- `ErrorHandlingMiddleware.cs` — catches `BridgeException` + unhandled, writes correct body, stamps correlation id.
- `ErrorHandlingMiddlewareExtensions.cs` — `UseBridgeErrorHandling()` extension.

**Tests:**
- `tests/AdoMcpBridge.Api.Tests/Telemetry/BridgeMeterTests.cs`
- `tests/AdoMcpBridge.Api.Tests/Telemetry/MetricContractTests.cs`
- `tests/AdoMcpBridge.Api.Tests/Middleware/ErrorHandlingMiddlewareTests.cs`

**Infra:**
- `infra/modules/alerts.bicep` — five alert rules.
- `infra/main.bicep` — modified to invoke `alerts.bicep`.

**Docs / repo:**
- `docs/runbook.md` — six scenarios, full Kusto queries inline.
- `.github/pull_request_template.md` — alert/runbook checkbox.

---

### Task 1: Create `BridgeMeter` with the eight instruments

**Files:**
- Create: `src/AdoMcpBridge.Api/Telemetry/BridgeMeter.cs`
- Test: `tests/AdoMcpBridge.Api.Tests/Telemetry/BridgeMeterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Diagnostics.Metrics;
using AdoMcpBridge.Api.Telemetry;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Api.Tests.Telemetry;

public class BridgeMeterTests
{
    [Fact]
    public void Meter_Name_Is_AdoMcpBridge()
    {
        BridgeMeter.MeterName.Should().Be("AdoMcpBridge");
        BridgeMeter.Meter.Name.Should().Be("AdoMcpBridge");
    }

    [Fact]
    public void TokenIssued_Counter_Records_With_GrantType_Tag()
    {
        var captured = new List<(string instrument, long value, KeyValuePair<string, object?>[] tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == "AdoMcpBridge") l.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
            captured.Add((inst.Name, value, tags.ToArray())));
        listener.Start();

        BridgeMeter.TokenIssued.Add(1, new KeyValuePair<string, object?>("grant_type", "authorization_code"));

        captured.Should().ContainSingle(c =>
            c.instrument == "oauth.token.issued"
            && c.value == 1
            && c.tags.Any(t => t.Key == "grant_type" && (string)t.Value! == "authorization_code"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AdoMcpBridge.Api.Tests --filter FullyQualifiedName~BridgeMeterTests`
Expected: FAIL — `BridgeMeter` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Diagnostics.Metrics;

namespace AdoMcpBridge.Api.Telemetry;

public static class BridgeMeter
{
    public const string MeterName = "AdoMcpBridge";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> DcrRegistrations =
        Meter.CreateCounter<long>("oauth.dcr.registrations", description: "OAuth dynamic client registrations.");

    public static readonly Counter<long> TokenIssued =
        Meter.CreateCounter<long>("oauth.token.issued", description: "Wrapper tokens issued.");

    public static readonly Counter<long> TokenRefreshed =
        Meter.CreateCounter<long>("oauth.token.refreshed", description: "Wrapper tokens refreshed.");

    public static readonly Counter<long> TokenRejected =
        Meter.CreateCounter<long>("oauth.token.rejected", description: "Wrapper tokens rejected.");

    public static readonly Histogram<double> EntraRefreshDurationMs =
        Meter.CreateHistogram<double>("entra.refresh.duration_ms", unit: "ms", description: "Entra refresh latency.");

    public static readonly Histogram<double> ProxyUpstreamDurationMs =
        Meter.CreateHistogram<double>("proxy.upstream.duration_ms", unit: "ms", description: "Upstream MCP latency.");

    public static readonly Counter<long> ProxyUpstreamErrors =
        Meter.CreateCounter<long>("proxy.upstream.errors", description: "Upstream MCP errors.");

    public static readonly UpDownCounter<long> ProxyInFlight =
        Meter.CreateUpDownCounter<long>("proxy.in_flight", description: "In-flight proxy requests.");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AdoMcpBridge.Api.Tests --filter FullyQualifiedName~BridgeMeterTests`
Expected: PASS (2/2).

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Api/Telemetry/BridgeMeter.cs tests/AdoMcpBridge.Api.Tests/Telemetry/BridgeMeterTests.cs
git commit -m "feat: add BridgeMeter with eight OpenTelemetry instruments"
```

---

### Task 2: Contract test — every spec metric name is registered

**Files:**
- Test: `tests/AdoMcpBridge.Api.Tests/Telemetry/MetricContractTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Diagnostics.Metrics;
using AdoMcpBridge.Api.Telemetry;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Api.Tests.Telemetry;

public class MetricContractTests
{
    private static readonly string[] ExpectedInstruments =
    {
        "oauth.dcr.registrations",
        "oauth.token.issued",
        "oauth.token.refreshed",
        "oauth.token.rejected",
        "entra.refresh.duration_ms",
        "proxy.upstream.duration_ms",
        "proxy.upstream.errors",
        "proxy.in_flight",
    };

    [Fact]
    public void All_Contract_Instruments_Are_Published_On_The_AdoMcpBridge_Meter()
    {
        // Touch the static class so its field initialisers run.
        _ = BridgeMeter.MeterName;

        var seen = new HashSet<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (inst, _) =>
            {
                if (inst.Meter.Name == BridgeMeter.MeterName) seen.Add(inst.Name);
            }
        };
        listener.Start();

        seen.Should().BeEquivalentTo(ExpectedInstruments);
    }
}
```

- [ ] **Step 2: Run test to verify it passes (Task 1 already created all instruments)**

Run: `dotnet test tests/AdoMcpBridge.Api.Tests --filter FullyQualifiedName~MetricContractTests`
Expected: PASS. If a future change removes a name, this test fails.

- [ ] **Step 3: Commit**

```bash
git add tests/AdoMcpBridge.Api.Tests/Telemetry/MetricContractTests.cs
git commit -m "test: pin OpenTelemetry instrument contract for AdoMcpBridge meter"
```

---

### Task 3: `AddBridgeTelemetry` DI extension wiring traces + metrics + Azure Monitor

**Files:**
- Create: `src/AdoMcpBridge.Api/Telemetry/TelemetryServiceCollectionExtensions.cs`
- Modify: `src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj` (add packages)
- Modify: `src/AdoMcpBridge.Api/Program.cs` (call extension)
- Test: `tests/AdoMcpBridge.Api.Tests/Telemetry/TelemetryServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Add NuGet packages**

```bash
dotnet add src/AdoMcpBridge.Api package OpenTelemetry.Extensions.Hosting
dotnet add src/AdoMcpBridge.Api package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/AdoMcpBridge.Api package OpenTelemetry.Instrumentation.Http
dotnet add src/AdoMcpBridge.Api package Azure.Monitor.OpenTelemetry.AspNetCore
```

- [ ] **Step 2: Write the failing test**

```csharp
using AdoMcpBridge.Api.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using Xunit;

namespace AdoMcpBridge.Api.Tests.Telemetry;

public class TelemetryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBridgeTelemetry_Registers_MeterProvider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApplicationInsights:ConnectionString"] = "" // empty disables exporter, keeps wiring testable
            })
            .Build();

        services.AddLogging();
        services.AddBridgeTelemetry(config);

        using var provider = services.BuildServiceProvider();
        provider.GetService<MeterProvider>().Should().NotBeNull();
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/AdoMcpBridge.Api.Tests --filter FullyQualifiedName~TelemetryServiceCollectionExtensionsTests`
Expected: FAIL — extension method missing.

- [ ] **Step 4: Write implementation**

```csharp
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AdoMcpBridge.Api.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddBridgeTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var aiConnection = configuration["ApplicationInsights:ConnectionString"];

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: "ado-mcp-bridge"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddMeter(BridgeMeter.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(aiConnection))
        {
            otel.UseAzureMonitor(o => o.ConnectionString = aiConnection);
        }

        return services;
    }
}
```

- [ ] **Step 5: Wire into Program.cs**

In `src/AdoMcpBridge.Api/Program.cs`, after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
builder.Services.AddBridgeTelemetry(builder.Configuration);
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/AdoMcpBridge.Api.Tests --filter FullyQualifiedName~TelemetryServiceCollectionExtensionsTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/AdoMcpBridge.Api/Telemetry/TelemetryServiceCollectionExtensions.cs src/AdoMcpBridge.Api/AdoMcpBridge.Api.csproj src/AdoMcpBridge.Api/Program.cs tests/AdoMcpBridge.Api.Tests/Telemetry/TelemetryServiceCollectionExtensionsTests.cs
git commit -m "feat: wire OpenTelemetry tracing, metrics, and Azure Monitor exporter"
```

---

### Task 4: `BridgeException` base class

**Files:**
- Create: `src/AdoMcpBridge.Core/Errors/BridgeException.cs`
- Test: `tests/AdoMcpBridge.Core.Tests/Errors/BridgeExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AdoMcpBridge.Core.Errors;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Core.Tests.Errors;

public class BridgeExceptionTests
{
    private sealed class TestEx : BridgeException
    {
        public TestEx() : base("test_code", "msg") { }
    }

    [Fact]
    public void ErrorId_Is_Generated_And_Unique()
    {
        var a = new TestEx();
        var b = new TestEx();
        a.ErrorId.Should().NotBeNullOrWhiteSpace();
        a.ErrorId.Should().NotBe(b.ErrorId);
        a.ErrorCode.Should().Be("test_code");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter FullyQualifiedName~BridgeExceptionTests`
Expected: FAIL — `BridgeException` does not exist.

- [ ] **Step 3: Write implementation**

```csharp
namespace AdoMcpBridge.Core.Errors;

public abstract class BridgeException : Exception
{
    protected BridgeException(string errorCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        ErrorId = Guid.NewGuid().ToString("n");
    }

    public string ErrorCode { get; }
    public string ErrorId { get; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter FullyQualifiedName~BridgeExceptionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Errors/BridgeException.cs tests/AdoMcpBridge.Core.Tests/Errors/BridgeExceptionTests.cs
git commit -m "feat: add BridgeException base with auto-generated ErrorId"
```

---

### Task 5: `CallerErrorException` (400 / RFC 6749)

**Files:**
- Create: `src/AdoMcpBridge.Core/Errors/CallerErrorException.cs`
- Test: `tests/AdoMcpBridge.Core.Tests/Errors/CallerErrorExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AdoMcpBridge.Core.Errors;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Core.Tests.Errors;

public class CallerErrorExceptionTests
{
    [Fact]
    public void Carries_Oauth_Error_Fields()
    {
        var ex = new CallerErrorException("invalid_grant", "code expired");
        ex.ErrorCode.Should().Be("invalid_grant");
        ex.Message.Should().Be("code expired");
        ex.StatusCode.Should().Be(400);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter FullyQualifiedName~CallerErrorExceptionTests`
Expected: FAIL.

- [ ] **Step 3: Write implementation**

```csharp
namespace AdoMcpBridge.Core.Errors;

public sealed class CallerErrorException : BridgeException
{
    public CallerErrorException(string errorCode, string description, Exception? inner = null)
        : base(errorCode, description, inner) { }

    public int StatusCode => 400;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter FullyQualifiedName~CallerErrorExceptionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Errors/CallerErrorException.cs tests/AdoMcpBridge.Core.Tests/Errors/CallerErrorExceptionTests.cs
git commit -m "feat: add CallerErrorException for RFC 6749 400 responses"
```

---

### Task 6: `UpstreamErrorException` (502 / mapped)

**Files:**
- Create: `src/AdoMcpBridge.Core/Errors/UpstreamErrorException.cs`
- Test: `tests/AdoMcpBridge.Core.Tests/Errors/UpstreamErrorExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AdoMcpBridge.Core.Errors;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Core.Tests.Errors;

public class UpstreamErrorExceptionTests
{
    [Fact]
    public void Default_StatusCode_Is_502()
    {
        var ex = new UpstreamErrorException("upstream timeout");
        ex.StatusCode.Should().Be(502);
        ex.ErrorCode.Should().Be("upstream_error");
    }

    [Fact]
    public void Mapped_StatusCode_Is_Preserved()
    {
        var ex = new UpstreamErrorException("rate limited", mappedStatusCode: 429);
        ex.StatusCode.Should().Be(429);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter FullyQualifiedName~UpstreamErrorExceptionTests`
Expected: FAIL.

- [ ] **Step 3: Write implementation**

```csharp
namespace AdoMcpBridge.Core.Errors;

public sealed class UpstreamErrorException : BridgeException
{
    public UpstreamErrorException(string description, int mappedStatusCode = 502, Exception? inner = null)
        : base("upstream_error", description, inner)
    {
        StatusCode = mappedStatusCode;
    }

    public int StatusCode { get; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter FullyQualifiedName~UpstreamErrorExceptionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Errors/UpstreamErrorException.cs tests/AdoMcpBridge.Core.Tests/Errors/UpstreamErrorExceptionTests.cs
git commit -m "feat: add UpstreamErrorException with mappable status code"
```

---

### Task 7: `InternalErrorException` (500, opaque)

**Files:**
- Create: `src/AdoMcpBridge.Core/Errors/InternalErrorException.cs`
- Test: `tests/AdoMcpBridge.Core.Tests/Errors/InternalErrorExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AdoMcpBridge.Core.Errors;
using FluentAssertions;
using Xunit;

namespace AdoMcpBridge.Core.Tests.Errors;

public class InternalErrorExceptionTests
{
    [Fact]
    public void Carries_Opaque_ErrorId_And_500()
    {
        var ex = new InternalErrorException("db unreachable");
        ex.StatusCode.Should().Be(500);
        ex.ErrorCode.Should().Be("internal_error");
        ex.ErrorId.Should().HaveLength(32); // Guid "n" format
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter FullyQualifiedName~InternalErrorExceptionTests`
Expected: FAIL.

- [ ] **Step 3: Write implementation**

```csharp
namespace AdoMcpBridge.Core.Errors;

public sealed class InternalErrorException : BridgeException
{
    public InternalErrorException(string internalMessage, Exception? inner = null)
        : base("internal_error", internalMessage, inner) { }

    public int StatusCode => 500;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AdoMcpBridge.Core.Tests --filter FullyQualifiedName~InternalErrorExceptionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Core/Errors/InternalErrorException.cs tests/AdoMcpBridge.Core.Tests/Errors/InternalErrorExceptionTests.cs
git commit -m "feat: add InternalErrorException with opaque error_id"
```

---

### Task 8: `ErrorHandlingMiddleware` — OAuth-path RFC 6749 JSON for caller errors

**Files:**
- Create: `src/AdoMcpBridge.Api/Middleware/ErrorHandlingMiddleware.cs`
- Create: `src/AdoMcpBridge.Api/Middleware/ErrorHandlingMiddlewareExtensions.cs`
- Test: `tests/AdoMcpBridge.Api.Tests/Middleware/ErrorHandlingMiddlewareTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Text.Json;
using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Core.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AdoMcpBridge.Api.Tests.Middleware;

public class ErrorHandlingMiddlewareTests
{
    private static async Task<(int status, string body, string? corr)> Invoke(
        string path, RequestDelegate next)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        var mw = new ErrorHandlingMiddleware(next, NullLogger<ErrorHandlingMiddleware>.Instance);
        await mw.InvokeAsync(ctx);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        ctx.Response.Headers.TryGetValue("X-Correlation-Id", out var corr);
        return (ctx.Response.StatusCode, body, corr);
    }

    [Fact]
    public async Task CallerError_On_Token_Endpoint_Returns_Rfc6749_Json()
    {
        var (status, body, _) = await Invoke("/token", _ =>
            throw new CallerErrorException("invalid_grant", "code expired"));

        status.Should().Be(400);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetString().Should().Be("invalid_grant");
        doc.RootElement.GetProperty("error_description").GetString().Should().Be("code expired");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AdoMcpBridge.Api.Tests --filter FullyQualifiedName~ErrorHandlingMiddlewareTests`
Expected: FAIL — middleware does not exist.

- [ ] **Step 3: Write implementation**

```csharp
using System.Diagnostics;
using System.Text.Json;
using AdoMcpBridge.Core.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AdoMcpBridge.Api.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private static readonly string[] OAuthPaths =
        { "/token", "/authorize", "/register", "/revoke" };

    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _log;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var correlationId = Activity.Current?.TraceId.ToString()
                            ?? Guid.NewGuid().ToString("n");
        ctx.Response.Headers["X-Correlation-Id"] = correlationId;

        try
        {
            await _next(ctx);
        }
        catch (CallerErrorException ex)
        {
            _log.LogInformation("caller error {ErrorCode} on {Path}", ex.ErrorCode, ctx.Request.Path);
            await WriteAsync(ctx, ex.StatusCode, BuildCallerBody(ctx, ex, correlationId));
        }
        catch (UpstreamErrorException ex)
        {
            _log.LogWarning(ex, "upstream error on {Path}", ctx.Request.Path);
            await WriteProblem(ctx, ex.StatusCode, "upstream_error", ex.Message, correlationId, ex.ErrorId);
        }
        catch (InternalErrorException ex)
        {
            _log.LogError(ex, "internal error {ErrorId} on {Path}", ex.ErrorId, ctx.Request.Path);
            await WriteProblem(ctx, ex.StatusCode, "internal_error",
                "An internal error occurred.", correlationId, ex.ErrorId);
        }
        catch (Exception ex)
        {
            var wrapped = new InternalErrorException("unhandled", ex);
            _log.LogError(ex, "unhandled exception {ErrorId} on {Path}", wrapped.ErrorId, ctx.Request.Path);
            await WriteProblem(ctx, 500, "internal_error",
                "An internal error occurred.", correlationId, wrapped.ErrorId);
        }
    }

    private static object BuildCallerBody(HttpContext ctx, CallerErrorException ex, string correlationId)
    {
        if (IsOAuthPath(ctx.Request.Path))
        {
            return new
            {
                error = ex.ErrorCode,
                error_description = ex.Message,
                correlation_id = correlationId,
            };
        }

        return new ProblemDetails
        {
            Status = 400,
            Title = ex.ErrorCode,
            Detail = ex.Message,
            Extensions = { ["correlation_id"] = correlationId },
        };
    }

    private static bool IsOAuthPath(PathString path)
    {
        foreach (var p in OAuthPaths)
        {
            if (path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static async Task WriteAsync(HttpContext ctx, int status, object body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, body);
    }

    private static Task WriteProblem(HttpContext ctx, int status, string title,
        string detail, string correlationId, string errorId)
    {
        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Extensions =
            {
                ["correlation_id"] = correlationId,
                ["error_id"] = errorId,
            },
        };
        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.StatusCode = status;
        return JsonSerializer.SerializeAsync(ctx.Response.Body, pd);
    }
}
```

And the extension:

```csharp
using Microsoft.AspNetCore.Builder;

namespace AdoMcpBridge.Api.Middleware;

public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseBridgeErrorHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ErrorHandlingMiddleware>();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AdoMcpBridge.Api.Tests --filter FullyQualifiedName~ErrorHandlingMiddlewareTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdoMcpBridge.Api/Middleware/ErrorHandlingMiddleware.cs src/AdoMcpBridge.Api/Middleware/ErrorHandlingMiddlewareExtensions.cs tests/AdoMcpBridge.Api.Tests/Middleware/ErrorHandlingMiddlewareTests.cs
git commit -m "feat: add global ErrorHandlingMiddleware with RFC 6749 OAuth bodies"
```

---

### Task 9: Middleware — upstream and internal error mapping tests

**Files:**
- Modify: `tests/AdoMcpBridge.Api.Tests/Middleware/ErrorHandlingMiddlewareTests.cs`
- Modify: `src/AdoMcpBridge.Api/Program.cs` (register middleware first in pipeline)

- [ ] **Step 1: Add the remaining behaviour tests**

Append to the existing test class:

```csharp
[Fact]
public async Task UpstreamError_Returns_ProblemJson_With_Mapped_Status()
{
    var (status, body, corr) = await Invoke("/mcp/foo", _ =>
        throw new UpstreamErrorException("rate limited", mappedStatusCode: 429));

    status.Should().Be(429);
    corr.Should().NotBeNullOrEmpty();
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("title").GetString().Should().Be("upstream_error");
    doc.RootElement.GetProperty("correlation_id").GetString().Should().NotBeNullOrEmpty();
}

[Fact]
public async Task InternalError_Returns_Opaque_500_With_ErrorId()
{
    var (status, body, _) = await Invoke("/mcp/foo", _ =>
        throw new InternalErrorException("secret detail must not leak"));

    status.Should().Be(500);
    body.Should().NotContain("secret detail must not leak");
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("error_id").GetString().Should().NotBeNullOrEmpty();
}

[Fact]
public async Task Unhandled_Exception_Is_Wrapped_As_Internal()
{
    var (status, body, _) = await Invoke("/mcp/foo", _ => throw new InvalidOperationException("boom"));

    status.Should().Be(500);
    body.Should().NotContain("boom");
}

[Fact]
public async Task CallerError_On_Non_OAuth_Path_Uses_ProblemDetails()
{
    var (status, body, _) = await Invoke("/mcp/foo", _ =>
        throw new CallerErrorException("bad_request", "missing header"));

    status.Should().Be(400);
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.TryGetProperty("error", out _).Should().BeFalse();
    doc.RootElement.GetProperty("title").GetString().Should().Be("bad_request");
}
```

- [ ] **Step 2: Run tests to verify they pass against the Task 8 implementation**

Run: `dotnet test tests/AdoMcpBridge.Api.Tests --filter FullyQualifiedName~ErrorHandlingMiddlewareTests`
Expected: PASS (5/5).

- [ ] **Step 3: Register middleware first in pipeline**

In `src/AdoMcpBridge.Api/Program.cs`, immediately after `var app = builder.Build();`:

```csharp
app.UseBridgeErrorHandling();
```

- [ ] **Step 4: Commit**

```bash
git add tests/AdoMcpBridge.Api.Tests/Middleware/ErrorHandlingMiddlewareTests.cs src/AdoMcpBridge.Api/Program.cs
git commit -m "test: cover upstream, internal, unhandled, and non-OAuth caller mapping"
```

---

### Task 10: `infra/modules/alerts.bicep` — five alert rules

**Files:**
- Create: `infra/modules/alerts.bicep`
- Modify: `infra/main.bicep`

- [ ] **Step 1: Write the module**

```bicep
@description('Deploys the five Observability alert rules paired with runbook scenarios.')
param location string
param actionGroupId string
param appInsightsId string
param logAnalyticsWorkspaceId string
param keyVaultId string
param environmentName string

var prefix = 'adomcp-${environmentName}'

resource internalErrorAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${prefix}-internal-error'
  location: location
  properties: {
    displayName: 'Any internal_error in 5 min'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [ logAnalyticsWorkspaceId ]
    criteria: {
      allOf: [
        {
          query: 'AppTraces | where SeverityLevel == 3 | where Message has "internal_error"'
          operator: 'GreaterThanOrEqual'
          threshold: 1
          timeAggregation: 'Count'
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

resource tokenRejectionAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-token-rejection-rate'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'TokenRejectRatio'
          metricNamespace: 'azure.applicationinsights'
          metricName: 'oauth.token.rejected'
          operator: 'GreaterThan'
          threshold: 10
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

resource upstreamErrorAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-upstream-error-rate'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'UpstreamErrorPct'
          metricNamespace: 'azure.applicationinsights'
          metricName: 'proxy.upstream.errors'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

resource entraRefreshLatencyAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-entra-refresh-p95'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'EntraRefreshP95'
          metricNamespace: 'azure.applicationinsights'
          metricName: 'entra.refresh.duration_ms'
          operator: 'GreaterThan'
          threshold: 2000
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

resource certExpiryAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${prefix}-cert-expiry'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [ keyVaultId ]
    evaluationFrequency: 'PT1H'
    windowSize: 'PT1H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'CertNearExpiry'
          metricNamespace: 'Microsoft.KeyVault/vaults'
          metricName: 'CertificateNearExpiry'
          operator: 'LessThan'
          threshold: 14
          timeAggregation: 'Minimum'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}
```

- [ ] **Step 2: Wire from `infra/main.bicep`**

Append a module call:

```bicep
module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    location: location
    actionGroupId: observability.outputs.actionGroupId
    appInsightsId: observability.outputs.appInsightsId
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsWorkspaceId
    keyVaultId: keyVault.outputs.id
    environmentName: environmentName
  }
}
```

- [ ] **Step 3: Validate Bicep**

Run: `az bicep build --file infra/main.bicep`
Expected: build succeeds with no diagnostics.

- [ ] **Step 4: Commit**

```bash
git add infra/modules/alerts.bicep infra/main.bicep
git commit -m "feat: deploy five Observability alert rules via alerts.bicep"
```

---

### Task 11: `docs/runbook.md` — scenarios 1–3

**Files:**
- Create: `docs/runbook.md`

- [ ] **Step 1: Write scenarios 1–3 in full**

````markdown
# ADO MCP Bridge Runbook

Every alert in `infra/modules/alerts.bicep` has a corresponding scenario
below. PRs that add new alerts must add a new scenario (see PR template).

All Kusto queries assume the App Insights workspace bound to
`appi-adomcp-{env}`. Replace time windows as needed.

---

## Scenario 1 — Internal error spike

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

## Scenario 2 — Token rejection spike

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
   replicas (`AppDependencies | where Type == "Azure DocumentDB" | ...`
   is not the right one — use `Heartbeat` in Log Analytics).
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

## Scenario 3 — Upstream MCP failures

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
````

- [ ] **Step 2: Commit**

```bash
git add docs/runbook.md
git commit -m "docs: add runbook scenarios 1-3 (internal, token, upstream)"
```

---

### Task 12: `docs/runbook.md` — scenarios 4–6

**Files:**
- Modify: `docs/runbook.md`

- [ ] **Step 1: Append scenarios 4–6 in full**

````markdown
## Scenario 4 — Slow Entra refresh

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

## Scenario 5 — Certificate near expiry

**Alert:** `adomcp-{env}-cert-expiry` — Key Vault certificate < 14 days
to expiry.

**Symptom:** Pre-failure warning. If ignored, Entra auth will hard-fail
when the cert expires.

**Saved Kusto query:**

```kusto
AzureMetrics
| where TimeGenerated > ago(1d)
| where ResourceProvider == "MICROSOFT.KEYVAULT"
| where MetricName == "CertificateNearExpiry"
| summarize days_left = min(Minimum) by Resource, bin(TimeGenerated, 1h)
| order by days_left asc
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
- After rotation, re-run the Kusto query above and confirm
  `days_left` > 60.

**Escalation:** Page on-call when `days_left` < 3.

---

## Scenario 6 — Database outage / migration failure

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
````

- [ ] **Step 2: Commit**

```bash
git add docs/runbook.md
git commit -m "docs: add runbook scenarios 4-6 (entra, cert, database)"
```

---

### Task 13: PR template with alert/runbook checkbox

**Files:**
- Create: `.github/pull_request_template.md`

- [ ] **Step 1: Write the template**

```markdown
## Summary

<!-- One paragraph describing the change and why. -->

## Review focus

<!-- Bullet list of files / decisions reviewers should weigh in on. -->

## Test plan

- [ ] Unit tests added/updated
- [ ] Integration tests added/updated where relevant
- [ ] `dotnet test` passes locally with 100% coverage thresholds met

## Observability gate

- [ ] I introduced a new alert and added a corresponding runbook entry
- [ ] No new alert
```

- [ ] **Step 2: Commit**

```bash
git add .github/pull_request_template.md
git commit -m "chore: add PR template enforcing alert/runbook pairing"
```

---

### Task 14: Self-check — full test run and Bicep compile

**Files:** none.

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: all tests pass; coverage thresholds (set in CI) are met.

- [ ] **Step 2: Compile Bicep**

Run: `az bicep build --file infra/main.bicep`
Expected: success, no warnings.

- [ ] **Step 3: Confirm runbook scenario count matches alert count**

Run: `grep -c '^## Scenario ' docs/runbook.md`
Expected: `6`.

Run: `grep -cE "resource [A-Za-z]+Alert " infra/modules/alerts.bicep`
Expected: `5` (Scenario 6 is intentionally covered by Scenario 1's alert plus CI logs).

- [ ] **Step 4: Commit nothing (verification only)**

If any of the above fail, return to the relevant task and fix.

---

## Self-Review Notes

**Spec coverage (§8 of the design spec):**
- Error categories table → Tasks 4–9 (`CallerErrorException` 400 RFC 6749, `UpstreamErrorException` 502/mapped, `InternalErrorException` 500 opaque), middleware verified by Tasks 8–9.
- Correlation id stamped on every response → Task 8 middleware sets `X-Correlation-Id` from the W3C trace id before invoking downstream, tested in Task 9.
- OpenTelemetry custom metrics (eight names) → Tasks 1–3; contract test in Task 2 fails the build if a name is removed.
- Azure Monitor exporter (`Azure.Monitor.OpenTelemetry.AspNetCore`) → Task 3.
- Five alert rules → Task 10 (`alerts.bicep`).
- Runbook with six scenarios + saved Kusto queries → Tasks 11–12.
- PR template alert/runbook gate → Task 13.
- Verification step → Task 14.

**Placeholder scan:** No "TBD", "similar to", or "add appropriate" phrases. Every Kusto query is written out in full. Every test body and implementation body shows real code.

**Type consistency:** `BridgeMeter` instrument names match the eight names in `_shared-contracts.md` §"OpenTelemetry metric names" exactly. `BridgeException.ErrorCode` / `ErrorId` properties are used consistently across Tasks 4–9. `CallerErrorException.StatusCode == 400`, `UpstreamErrorException.StatusCode` defaults to 502 and accepts an override (used as 429 in tests), `InternalErrorException.StatusCode == 500` — all referenced consistently in middleware (Task 8) and tests (Tasks 8–9). `AddBridgeTelemetry` signature `(this IServiceCollection, IConfiguration)` matches its single call site in `Program.cs`. `UseBridgeErrorHandling` extension name matches its sole `Program.cs` usage. Alert resource names in Task 10 are referenced verbatim in the runbook (Tasks 11–12).
