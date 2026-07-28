# Phase 2F.1 — OpenTelemetry Foundation + Prometheus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the existing `MSOSync.Metrics` project (which already has OTel packages referenced) into the host, implement `OtelMetricsService` as a drop-in replacement for `InMemoryMetricsService`, and expose a Prometheus scrape endpoint at `/metrics`.

**Architecture:** `OtelMetricsService` implements `IMetricsService` using `System.Diagnostics.Metrics.Meter`. When `Telemetry:Enabled = true`, OTel is registered and `OtelMetricsService` replaces `InMemoryMetricsService` in DI. When disabled (default), existing behaviour is unchanged. No call-site changes anywhere in the codebase.

**Tech Stack:** C# 13 / .NET 9 / OpenTelemetry.Extensions.Hosting / OpenTelemetry.Instrumentation.AspNetCore / OpenTelemetry.Instrumentation.Runtime / OpenTelemetry.Instrumentation.EntityFrameworkCore / OpenTelemetry.Exporter.Prometheus.AspNetCore / OpenTelemetry.Exporter.OpenTelemetryProtocol / System.Diagnostics.Metrics

## Global Constraints

- `IMetricsService` interface in `MSOSync.Common` — unchanged, zero call-site changes
- `Meter` name: `"MSOSync"`, version `"1.0"`
- `Telemetry:Enabled` defaults `false` — when false, `InMemoryMetricsService` stays active
- Prometheus endpoint: `/metrics` (not authed — firewall/IP-restrict in production)
- `InMemoryMetricsService` retained in `MSOSync.Common` for test injection
- `git add` by file name only

---

### Task 1: OtelMetricsService

**Files:**
- Create: `src/MSOSync.Metrics/OtelMetricsService.cs`
- Modify: `src/MSOSync.Metrics/MSOSync.Metrics.csproj` (add missing packages)
- Create: `tests/MSOSync.MetricsTests/OtelMetricsServiceTests.cs` (new test file in existing test project, or create project if absent)

**Interfaces:**
- Consumes: `IMetricsService` from `MSOSync.Common`
- Produces: `OtelMetricsService : IMetricsService`

- [ ] **Step 1: Verify/update MSOSync.Metrics.csproj packages**

Read `src/MSOSync.Metrics/MSOSync.Metrics.csproj`. Ensure these packages are present:

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.10.1" />
<PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.10.0-rc.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.10.0-beta.1" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />
<ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
```

Add any missing ones. Check which packages are already there to avoid duplicates.

- [ ] **Step 2: Write failing tests for OtelMetricsService**

Create `tests/MSOSync.MetricsTests/OtelMetricsServiceTests.cs` (if `MSOSync.MetricsTests` project doesn't exist, create it following the same pattern as other test projects):

```csharp
// tests/MSOSync.MetricsTests/OtelMetricsServiceTests.cs
using System.Diagnostics.Metrics;
using FluentAssertions;
using MSOSync.Metrics;
using Xunit;

namespace MSOSync.MetricsTests;

public sealed class OtelMetricsServiceTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _recorded = [];

    public OtelMetricsServiceTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "MSOSync") listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            _recorded.Add((instrument.Name, value, tags.ToArray())));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _recorded.Add((instrument.Name, (double)value, tags.ToArray())));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void RecordHistogram_EmitsMeasurement_WithCorrectName()
    {
        var svc = new OtelMetricsService();

        svc.RecordHistogram("sync.pipeline.fetch_ms", 42.5);

        _listener.RecordObservableInstruments();
        _recorded.Should().ContainSingle(r => r.Name == "sync.pipeline.fetch_ms" && r.Value == 42.5);
    }

    [Fact]
    public void IncrementCounter_EmitsMeasurement_WithCorrectName()
    {
        var svc = new OtelMetricsService();

        svc.IncrementCounter("sync.batches.sent");

        _listener.RecordObservableInstruments();
        _recorded.Should().ContainSingle(r => r.Name == "sync.batches.sent" && r.Value == 1.0);
    }

    [Fact]
    public void RecordHistogram_IncludesTags_WhenProvided()
    {
        var svc = new OtelMetricsService();

        svc.RecordHistogram("sync.pipeline.send_ms", 10.0,
            new Dictionary<string, string> { ["node_id"] = "node-1" });

        _listener.RecordObservableInstruments();
        var entry = _recorded.First(r => r.Name == "sync.pipeline.send_ms");
        entry.Tags.Should().Contain(t => t.Key == "node_id" && (string?)t.Value == "node-1");
    }
}
```

- [ ] **Step 3: Run tests — expect compile failure (OtelMetricsService missing)**

```
dotnet test tests/MSOSync.MetricsTests -v minimal 2>&1 | head -5
```

- [ ] **Step 4: Implement OtelMetricsService**

```csharp
// src/MSOSync.Metrics/OtelMetricsService.cs
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using MSOSync.Common;

namespace MSOSync.Metrics;

public sealed class OtelMetricsService : IMetricsService
{
    private static readonly Meter _meter = new("MSOSync", "1.0");
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();

    public void IncrementCounter(string name, Dictionary<string, string>? tags = null)
    {
        var counter = _counters.GetOrAdd(name, static n => _meter.CreateCounter<long>(n));
        counter.Add(1, ToTagList(tags));
    }

    public void RecordHistogram(string name, double valueMs, Dictionary<string, string>? tags = null)
    {
        var histogram = _histograms.GetOrAdd(name, static n => _meter.CreateHistogram<double>(n, unit: "ms"));
        histogram.Record(valueMs, ToTagList(tags));
    }

    private static TagList ToTagList(Dictionary<string, string>? tags)
    {
        if (tags is null or { Count: 0 }) return default;
        var tagList = new TagList();
        foreach (var (k, v) in tags) tagList.Add(k, v);
        return tagList;
    }
}
```

- [ ] **Step 5: Run tests — all pass**

```
dotnet test tests/MSOSync.MetricsTests -v minimal
```
Expected: `Passed: 3, Failed: 0`

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Metrics/OtelMetricsService.cs src/MSOSync.Metrics/MSOSync.Metrics.csproj tests/MSOSync.MetricsTests/OtelMetricsServiceTests.cs
git commit -m "feat(2F.1-T1): add OtelMetricsService backed by System.Diagnostics.Metrics"
```

---

### Task 2: OTel pipeline wiring + Prometheus endpoint

**Files:**
- Create: `src/MSOSync.Metrics/TelemetryOptions.cs`
- Create: `src/MSOSync.Metrics/MetricsServiceExtensions.cs`
- Modify: `src/MSOSync.App/MSOSync.App.csproj` (add project reference to MSOSync.Metrics)
- Modify: `src/MSOSync.App/Program.cs` (call AddTelemetry + map Prometheus endpoint)
- Modify: `src/MSOSync.App/appsettings.json` (add Telemetry section)

**Interfaces:**
- Consumes: `OtelMetricsService`, `IMetricsService` (Task 1)
- Produces: `AddTelemetry(IServiceCollection, IConfiguration)` extension; `/metrics` Prometheus endpoint

- [ ] **Step 1: Define TelemetryOptions**

```csharp
// src/MSOSync.Metrics/TelemetryOptions.cs
namespace MSOSync.Metrics;

public sealed class TelemetryOptions
{
    public const string Section = "Telemetry";

    public bool Enabled { get; set; } = false;
    public string ServiceName { get; set; } = "MSOSync";
    public string ServiceVersion { get; set; } = "1.0.0";
    public string OtlpEndpoint { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Add Telemetry section to appsettings.json**

Open `src/MSOSync.App/appsettings.json`. Add:

```json
"Telemetry": {
  "Enabled": false,
  "ServiceName": "MSOSync",
  "ServiceVersion": "1.0.0",
  "OtlpEndpoint": ""
}
```

- [ ] **Step 3: Write MetricsServiceExtensions**

```csharp
// src/MSOSync.Metrics/MetricsServiceExtensions.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MSOSync.Metrics;

public static class MetricsServiceExtensions
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddOptions<TelemetryOptions>()
            .BindConfiguration(TelemetryOptions.Section)
            .ValidateOnStart();

        var opts = config.GetSection(TelemetryOptions.Section).Get<TelemetryOptions>() ?? new();

        if (!opts.Enabled)
        {
            // Community Edition default: keep InMemoryMetricsService (already registered elsewhere)
            return services;
        }

        // Replace InMemoryMetricsService with OtelMetricsService
        // Remove any existing IMetricsService registration if present, then add OtelMetricsService
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMetricsService));
        if (descriptor is not null) services.Remove(descriptor);
        services.AddSingleton<IMetricsService, OtelMetricsService>();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(opts.ServiceName, serviceVersion: opts.ServiceVersion))
            .WithMetrics(b => b
                .AddMeter("MSOSync")
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter())
            .WithTracing(b =>
            {
                b.AddAspNetCoreInstrumentation()
                 .AddEntityFrameworkCoreInstrumentation()
                 .AddSource("MSOSync.Pipeline");

                if (!string.IsNullOrEmpty(opts.OtlpEndpoint))
                    b.AddOtlpExporter(o => o.Endpoint = new Uri(opts.OtlpEndpoint));
            });

        return services;
    }

    public static IApplicationBuilder UseTelemetry(this WebApplication app)
    {
        var opts = app.Configuration.GetSection(TelemetryOptions.Section).Get<TelemetryOptions>() ?? new();
        if (opts.Enabled)
            app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }
}
```

- [ ] **Step 4: Add MSOSync.Metrics reference to MSOSync.App.csproj**

Open `src/MSOSync.App/MSOSync.App.csproj`. Add inside existing `<ItemGroup>` with project references:

```xml
<ProjectReference Include="..\MSOSync.Metrics\MSOSync.Metrics.csproj" />
```

Verify `MSOSync.Metrics.csproj` doesn't already have this reference.

- [ ] **Step 5: Register telemetry in Program.cs**

Open `src/MSOSync.App/Program.cs`. Add `using MSOSync.Metrics;` at the top.

Find where services are registered (before `var app = builder.Build()`). Add:

```csharp
builder.Services.AddTelemetry(builder.Configuration);
```

After `var app = builder.Build();` and before `app.Run()`, add:

```csharp
app.UseTelemetry();
```

- [ ] **Step 6: Build and verify**

```
dotnet build src/MSOSync.App/MSOSync.App.csproj 2>&1 | tail -5
```
Expected: `Build succeeded.`

- [ ] **Step 7: Run full test suite (excluding integration tests)**

```
dotnet test --filter "FullyQualifiedName!~MSOSync.IntegrationTests" -v minimal 2>&1 | tail -10
```
Expected: no regressions.

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Metrics/TelemetryOptions.cs src/MSOSync.Metrics/MetricsServiceExtensions.cs src/MSOSync.App/MSOSync.App.csproj src/MSOSync.App/Program.cs src/MSOSync.App/appsettings.json
git commit -m "feat(2F.1-T2): wire OTel pipeline + Prometheus /metrics endpoint"
```

---

### Task 3: MetricsController update + integration smoke test

**Files:**
- Modify: `src/MSOSync.Api/Controllers/MetricsController.cs` (return 204 + Grafana hint when OTel enabled)
- Modify: `src/MSOSync.App/appsettings.json` (add Observability section)
- Create: `tests/MSOSync.MetricsTests/MetricsEndpointTests.cs`

**Interfaces:**
- Consumes: `TelemetryOptions`, `IMetricsService`, `/metrics` endpoint (Task 2)

- [ ] **Step 1: Add Observability section to appsettings.json**

```json
"Observability": {
  "GrafanaUrl": ""
}
```

- [ ] **Step 2: Update MetricsController**

Read the current `src/MSOSync.Api/Controllers/MetricsController.cs`. The controller currently returns in-memory metrics snapshot. Add logic:

```csharp
// Add IOptions<TelemetryOptions> injection to constructor
// Add IConfiguration injection to read Observability:GrafanaUrl

[HttpGet]
public IActionResult Get()
{
    if (_telemetryOptions.Value.Enabled)
    {
        // OTel active — in-memory snapshot is empty; direct client to Prometheus
        var grafanaUrl = _config["Observability:GrafanaUrl"];
        return Ok(new
        {
            message = "Telemetry is enabled. Metrics are available at /metrics (Prometheus format).",
            prometheusEndpoint = "/metrics",
            grafanaUrl = string.IsNullOrEmpty(grafanaUrl) ? null : grafanaUrl
        });
    }

    // Existing InMemoryMetricsService snapshot behaviour
    return Ok(_metricsService.GetSnapshot()); // or whatever the current return is
}
```

Read the file first to understand the existing method signature and return type before editing.

- [ ] **Step 3: Write endpoint smoke test**

```csharp
// tests/MSOSync.MetricsTests/MetricsEndpointTests.cs
using FluentAssertions;
using Xunit;

namespace MSOSync.MetricsTests;

public sealed class MetricsEndpointTests
{
    [Fact]
    public void OtelMetricsService_DoesNotThrow_OnConcurrentCalls()
    {
        var svc = new MSOSync.Metrics.OtelMetricsService();

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            svc.IncrementCounter("test.counter");
            svc.RecordHistogram("test.histogram", i * 1.5);
        }));

        var act = () => Task.WhenAll(tasks).GetAwaiter().GetResult();

        act.Should().NotThrow();
    }
}
```

- [ ] **Step 4: Run metrics tests**

```
dotnet test tests/MSOSync.MetricsTests -v minimal
```
Expected: `Passed: 4, Failed: 0`

- [ ] **Step 5: Commit**

```
git add src/MSOSync.Api/Controllers/MetricsController.cs src/MSOSync.App/appsettings.json tests/MSOSync.MetricsTests/MetricsEndpointTests.cs
git commit -m "feat(2F.1-T3): update MetricsController for OTel mode + concurrency test"
```
