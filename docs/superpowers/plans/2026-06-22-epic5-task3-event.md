# Task 3: MSOSync.Event

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `src/MSOSync.Event/IEventReader.cs`
- Create: `src/MSOSync.Event/EventReader.cs`
- Create: `src/MSOSync.Event/IEventPurger.cs`
- Create: `src/MSOSync.Event/EventPurger.cs`
- Create: `src/MSOSync.Event/EventServiceExtensions.cs`
- Delete: `src/MSOSync.Event/Placeholder.cs`

**Interfaces:**
- Consumes: `AppDbContext.DataEvents` (`DbSet<SyncDataEvent>`), `AppDbContext.Parameters` (for `retention_days`), `IClock` from Task 1
- Produces:
  - `IEventReader.ReadAsync(int batchSize, CancellationToken)` → `IReadOnlyList<SyncDataEvent>`
  - `IEventPurger.PurgeAsync(CancellationToken)` → `int` (rows deleted)
  - `AddEventServices(IServiceCollection)` extension

---

- [ ] **Step 1: Create `IEventReader`**

```csharp
// src/MSOSync.Event/IEventReader.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Event;

public interface IEventReader
{
    Task<IReadOnlyList<SyncDataEvent>> ReadAsync(int batchSize, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create `EventReader`**

```csharp
// src/MSOSync.Event/EventReader.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Event;

public sealed class EventReader(AppDbContext db) : IEventReader
{
    public async Task<IReadOnlyList<SyncDataEvent>> ReadAsync(int batchSize, CancellationToken ct = default)
    {
        return await db.DataEvents
            .AsNoTracking()
            .Where(e => !e.IsProcessed)
            .OrderBy(e => e.EventId)
            .Take(batchSize)
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 3: Create `IEventPurger`**

```csharp
// src/MSOSync.Event/IEventPurger.cs
namespace MSOSync.Event;

public interface IEventPurger
{
    Task<int> PurgeAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `EventPurger`**

Reads retention days from `sync_parameter` where `parameter_name = 'event.retention.days'` (default 30 if missing).

```csharp
// src/MSOSync.Event/EventPurger.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Event;

public sealed class EventPurger(AppDbContext db, IClock clock, ILogger<EventPurger> logger) : IEventPurger
{
    private const int DefaultRetentionDays = 30;
    private const string RetentionParam = "event.retention.days";

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var param = await db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == RetentionParam, ct);

        var retentionDays = int.TryParse(param?.ParameterValue, out var d) ? d : DefaultRetentionDays;
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        var deleted = await db.DataEvents
            .Where(e => e.IsProcessed && e.CreateTime < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("EventPurger deleted {Count} events older than {Cutoff:u}", deleted, cutoff);
        return deleted;
    }
}
```

- [ ] **Step 5: Create `EventServiceExtensions`**

```csharp
// src/MSOSync.Event/EventServiceExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Event;

public static class EventServiceExtensions
{
    public static IServiceCollection AddEventServices(this IServiceCollection services)
    {
        services.AddScoped<IEventReader, EventReader>();
        services.AddScoped<IEventPurger, EventPurger>();
        return services;
    }
}
```

- [ ] **Step 6: Delete `Placeholder.cs`**

```pwsh
Remove-Item src/MSOSync.Event/Placeholder.cs
```

- [ ] **Step 7: Build**

```pwsh
dotnet build src/MSOSync.Event/MSOSync.Event.csproj -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```pwsh
git add src/MSOSync.Event/IEventReader.cs `
        src/MSOSync.Event/EventReader.cs `
        src/MSOSync.Event/IEventPurger.cs `
        src/MSOSync.Event/EventPurger.cs `
        src/MSOSync.Event/EventServiceExtensions.cs
git rm src/MSOSync.Event/Placeholder.cs
git commit -m "feat(event): add EventReader and EventPurger"
```
