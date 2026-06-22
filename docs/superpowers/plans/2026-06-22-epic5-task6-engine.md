# Task 6: MSOSync.Engine

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `src/MSOSync.Engine/ITransportService.cs`
- Create: `src/MSOSync.Engine/NoOpTransportService.cs`
- Create: `src/MSOSync.Engine/SyncCycleCompletedEvent.cs`
- Create: `src/MSOSync.Engine/SyncEngine.cs`
- Create: `src/MSOSync.Engine/SyncEngineExtensions.cs`
- Modify: `src/MSOSync.Engine/MSOSync.Engine.csproj`
- Delete: `src/MSOSync.Engine/Placeholder.cs`

**Interfaces:**
- Consumes (from earlier tasks): `ITriggerDriftDetector`, `IEventReader`, `IRoutingService`, `IBatchCreator`, `SyncOutgoingBatch` (Persistence), `IMediator`
- Produces:
  - `ITransportService.SendBatchAsync(SyncOutgoingBatch, CancellationToken)`
  - `NoOpTransportService` — logs at Trace, no-op; Epic 6 replaces this registration only
  - `SyncCycleCompletedEvent(int EventsRead, int BatchesCreated, TimeSpan Duration) : INotification` — published after each run; no handler in this epic
  - `SyncEngine.RunAsync(CancellationToken)` — strict order: drift → read → route → create → transport → publish
  - `AddSyncEngine(IServiceCollection, IConfiguration)` extension

---

- [ ] **Step 1: Update `MSOSync.Engine.csproj`**

Current csproj only references Common. Add Trigger, Event, Routing, Batch (which transitively bring Persistence, MediatR):

```xml
<!-- src/MSOSync.Engine/MSOSync.Engine.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>SyncEngine, ApplyEngine, ApplyPipeline orchestration — depends on interfaces only</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MediatR" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Trigger\MSOSync.Trigger.csproj" />
    <ProjectReference Include="..\MSOSync.Event\MSOSync.Event.csproj" />
    <ProjectReference Include="..\MSOSync.Routing\MSOSync.Routing.csproj" />
    <ProjectReference Include="..\MSOSync.Batch\MSOSync.Batch.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `ITransportService`**

```csharp
// src/MSOSync.Engine/ITransportService.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public interface ITransportService
{
    Task SendBatchAsync(SyncOutgoingBatch batch, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `NoOpTransportService`**

```csharp
// src/MSOSync.Engine/NoOpTransportService.cs
using Microsoft.Extensions.Logging;
using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public sealed class NoOpTransportService(ILogger<NoOpTransportService> logger) : ITransportService
{
    public Task SendBatchAsync(SyncOutgoingBatch batch, CancellationToken ct = default)
    {
        logger.LogTrace("Transport not implemented. Batch {BatchId} skipped.", batch.BatchId);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Create `SyncCycleCompletedEvent`**

```csharp
// src/MSOSync.Engine/SyncCycleCompletedEvent.cs
using MediatR;

namespace MSOSync.Engine;

public sealed record SyncCycleCompletedEvent(
    int EventsRead,
    int BatchesCreated,
    TimeSpan Duration) : INotification;
```

- [ ] **Step 5: Create `SyncEngine`**

Strict orchestration order:
1. Drift detection (log only — never blocks pipeline)
2. Read unprocessed events
3. Resolve routes per event
4. Create batches
5. Send each batch (no-op this epic)
6. Publish `SyncCycleCompletedEvent`

```csharp
// src/MSOSync.Engine/SyncEngine.cs
using MediatR;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Event;
using MSOSync.Routing;
using MSOSync.Trigger;

namespace MSOSync.Engine;

public sealed class SyncEngine(
    ITriggerDriftDetector driftDetector,
    IEventReader eventReader,
    IRoutingService routingService,
    IBatchCreator batchCreator,
    ITransportService transport,
    IMediator mediator,
    IClock clock,
    ILogger<SyncEngine> logger)
{
    private const int BatchReadSize = 1000;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var start = clock.UtcNow;
        logger.LogDebug("SyncEngine.RunAsync starting");

        // 1. Drift detection — log only, never block
        try { await driftDetector.DetectAllAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Drift detection failed — continuing"); }

        // 2. Read unprocessed events
        var events = await eventReader.ReadAsync(BatchReadSize, ct);
        if (events.Count == 0)
        {
            logger.LogDebug("SyncEngine: no events to process");
            await mediator.Publish(new SyncCycleCompletedEvent(0, 0, clock.UtcNow - start), ct);
            return;
        }

        // 3. Resolve routes for each event
        var routes = new Dictionary<long, IReadOnlyList<string>>();
        foreach (var evt in events)
            routes[evt.EventId] = await routingService.ResolveAsync(evt.TriggerId, ct);

        // 4. Create batches
        var batches = await batchCreator.CreateBatchesAsync(events, routes, ct);

        // 5. Send each batch (no-op this epic)
        foreach (var batch in batches)
            await transport.SendBatchAsync(batch, ct);

        // 6. Publish cycle event
        var duration = clock.UtcNow - start;
        logger.LogInformation("SyncEngine: read={Events} batches={Batches} elapsed={Elapsed}",
            events.Count, batches.Count, duration);
        await mediator.Publish(new SyncCycleCompletedEvent(events.Count, batches.Count, duration), ct);
    }
}
```

- [ ] **Step 6: Create `SyncEngineExtensions`**

```csharp
// src/MSOSync.Engine/SyncEngineExtensions.cs
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Engine;

public static class SyncEngineExtensions
{
    public static IServiceCollection AddSyncEngine(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SyncEngine>());
        services.AddScoped<ITransportService, NoOpTransportService>();
        services.AddScoped<SyncEngine>();
        return services;
    }
}
```

- [ ] **Step 7: Delete `Placeholder.cs`**

```pwsh
Remove-Item src/MSOSync.Engine/Placeholder.cs
```

- [ ] **Step 8: Build**

```pwsh
dotnet build src/MSOSync.Engine/MSOSync.Engine.csproj -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 9: Commit**

```pwsh
git add src/MSOSync.Engine/MSOSync.Engine.csproj `
        src/MSOSync.Engine/ITransportService.cs `
        src/MSOSync.Engine/NoOpTransportService.cs `
        src/MSOSync.Engine/SyncCycleCompletedEvent.cs `
        src/MSOSync.Engine/SyncEngine.cs `
        src/MSOSync.Engine/SyncEngineExtensions.cs
git rm src/MSOSync.Engine/Placeholder.cs
git commit -m "feat(engine): add SyncEngine, NoOpTransportService, SyncCycleCompletedEvent"
```
