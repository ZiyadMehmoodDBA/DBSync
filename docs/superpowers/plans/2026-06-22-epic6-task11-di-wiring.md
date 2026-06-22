# Task 11: DI Wiring + csproj Updates

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 8
**Depends on:** Tasks 1–10 (all components must exist before wiring)

**Files:**
- Create: `src/MSOSync.Transport/TransportServiceExtensions.cs`
- Modify: `src/MSOSync.Batch/BatchPipelineExtensions.cs` (add `IBatchTransportQueryService`)
- Modify: `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs` (add `PullJob`)
- Modify: `src/MSOSync.App/Program.cs` (add `Configure<NodeProperties>` + `AddTransportServices` + `AddTopologyServices`)
- Modify: `src/MSOSync.App/MSOSync.App.csproj` (add Transport + Metadata + Topology references)

---

- [ ] **Step 1: Create TransportServiceExtensions**

Create `src/MSOSync.Transport/TransportServiceExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace MSOSync.Transport;

public static class TransportServiceExtensions
{
    public static IServiceCollection AddTransportServices(
        this IServiceCollection services,
        IConfiguration _)
    {
        // Singletons
        services.AddSingleton<GzipCompressionService>();
        services.AddSingleton<ITransportFailureClassifier, TransportFailureClassifier>();

        // Typed HttpClient with Polly resilience
        services.AddHttpClient<NodeHttpClient>()
            .AddResilienceHandler("transport", builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay            = TimeSpan.FromSeconds(1),
                    BackoffType      = DelayBackoffType.Linear,
                    UseJitter        = false,
                    DelayGenerator   = static args => args.AttemptNumber switch
                    {
                        0 => new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(1)),
                        1 => new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(2)),
                        _ => new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(5))
                    }
                });
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    HandledEventsAllowedBeforeBreaking = 5,
                    BreakDuration                      = TimeSpan.FromSeconds(30)
                });
            });

        services.AddScoped<INodeHttpClient, NodeHttpClient>();

        // Transport services (scoped — one per request / scope)
        services.AddScoped<PushClient>();
        services.AddScoped<PullClient>();
        services.AddScoped<AcknowledgementService>();
        services.AddScoped<IApplyService, NoOpApplyService>();

        // SmartTransportService registered as the ITransportService implementation
        // (replaces NoOpTransportService removed from AddSyncEngine in Task 8)
        services.AddScoped<ITransportService, SmartTransportService>();

        return services;
    }
}
```

Note: `ITransportService` is in `MSOSync.Engine`; Transport.csproj references Engine (added in Task 3).

If `Microsoft.Extensions.Http.Resilience` API differs from the above (it's a .NET 9 package and may have slightly different option names), use the equivalent:
```csharp
        services.AddHttpClient<NodeHttpClient>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.MinimumThroughput = 5;
            });
```

Use whichever API compiles without warnings.

- [ ] **Step 2: Update BatchPipelineExtensions**

Add `IBatchTransportQueryService` registration to `src/MSOSync.Batch/BatchPipelineExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Batch;

public static class BatchPipelineExtensions
{
    public static IServiceCollection AddBatchPipeline(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddScoped<IBatchStateMachine, BatchStateMachine>();
        services.AddScoped<IBatchCreator, BatchCreator>();
        services.AddScoped<RetryProcessor>();
        services.AddScoped<BatchPurger>();
        services.AddScoped<IBatchTransportQueryService, BatchTransportQueryService>();
        return services;
    }
}
```

- [ ] **Step 3: Update SyncSchedulerExtensions**

Add `PullJob` to `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Scheduler;

public static class SyncSchedulerExtensions
{
    public static IServiceCollection AddSyncScheduler(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SchedulerRecovery>());
        services.AddHostedService<SchedulerRecovery>();  // runs first on startup
        services.AddHostedService<SyncJob>();
        services.AddHostedService<RetryJob>();
        services.AddHostedService<PurgeJob>();
        services.AddHostedService<PullJob>();
        return services;
    }
}
```

- [ ] **Step 4: Update App.csproj**

Add Transport, Metadata, and Topology references to `src/MSOSync.App/MSOSync.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <Description>ASP.NET Core entry point — wires DI, starts BackgroundService workers</Description>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Sinks.File" />
    <PackageReference Include="Serilog.Enrichers.Thread" />
    <PackageReference Include="Serilog.Enrichers.Environment" />
    <PackageReference Include="FluentValidation.AspNetCore" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Api\MSOSync.Api.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Trigger\MSOSync.Trigger.csproj" />
    <ProjectReference Include="..\MSOSync.Event\MSOSync.Event.csproj" />
    <ProjectReference Include="..\MSOSync.Routing\MSOSync.Routing.csproj" />
    <ProjectReference Include="..\MSOSync.Batch\MSOSync.Batch.csproj" />
    <ProjectReference Include="..\MSOSync.Engine\MSOSync.Engine.csproj" />
    <ProjectReference Include="..\MSOSync.Scheduler\MSOSync.Scheduler.csproj" />
    <ProjectReference Include="..\MSOSync.Metrics\MSOSync.Metrics.csproj" />
    <ProjectReference Include="..\MSOSync.Transport\MSOSync.Transport.csproj" />
    <ProjectReference Include="..\MSOSync.Metadata\MSOSync.Metadata.csproj" />
    <ProjectReference Include="..\MSOSync.Topology\MSOSync.Topology.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Update Program.cs**

In `src/MSOSync.App/Program.cs`, add two new using statements at the top:
```csharp
using MSOSync.Common;
using MSOSync.Topology;
using MSOSync.Transport;
```

Add two registrations after `builder.Services.AddSyncEngine(builder.Configuration)`:
```csharp
    builder.Services.Configure<NodeProperties>(builder.Configuration.GetSection("Node"));
    builder.Services.AddTransportServices(builder.Configuration);
    builder.Services.AddTopologyServices();
```

The complete relevant block in Program.cs (after the change):
```csharp
    builder.Services.AddMetadata(builder.Configuration);
    builder.Services.AddSingleton<IClock, SystemClock>();
    builder.Services.AddTriggerEngine(builder.Configuration);
    builder.Services.AddEventServices();
    builder.Services.AddRoutingServices();
    builder.Services.AddBatchPipeline(builder.Configuration);
    builder.Services.AddSyncEngine(builder.Configuration);
    builder.Services.AddSyncScheduler(builder.Configuration);
    builder.Services.Configure<NodeProperties>(builder.Configuration.GetSection("Node"));
    builder.Services.AddTransportServices(builder.Configuration);
    builder.Services.AddTopologyServices();
    builder.Services.AddHostedService<AdminBootstrapper>();
```

- [ ] **Step 6: Add appsettings configuration section**

The `Node:` section in `appsettings.json` (or via environment variables). Check if `src/MSOSync.App/appsettings.json` exists and add:
```json
{
  "Node": {
    "Id": "",
    "GroupId": "",
    "SyncUrl": ""
  }
}
```

`NodeToken` intentionally omitted — loaded ONLY via `MSOSYNC_NODE_TOKEN` env var mapping to `Node:NodeToken`.

If `appsettings.Development.json` exists, add the same section with dev values:
```json
{
  "Node": {
    "Id": "local-dev",
    "GroupId": "dev",
    "SyncUrl": "http://localhost:5000"
  }
}
```

- [ ] **Step 7: Build and run all tests**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.EngineTests -c Debug
```

Expected: build clean, all EngineTests pass.

- [ ] **Step 8: Commit**

```pwsh
git add src/MSOSync.Transport/TransportServiceExtensions.cs
git add src/MSOSync.Batch/BatchPipelineExtensions.cs
git add src/MSOSync.Scheduler/SyncSchedulerExtensions.cs
git add src/MSOSync.App/Program.cs
git add src/MSOSync.App/MSOSync.App.csproj
git commit -m "feat(epic6): wire TransportServices, TopologyServices, PullJob, NodeProperties into DI"
```
