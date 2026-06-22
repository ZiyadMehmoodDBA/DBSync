# Task 1: IClock + DatabaseLockProvider

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `src/MSOSync.Common/IClock.cs`
- Create: `src/MSOSync.Common/SystemClock.cs`
- Create: `src/MSOSync.Persistence/Lock/LockNames.cs`
- Create: `src/MSOSync.Persistence/Lock/IDatabaseLockProvider.cs`
- Create: `src/MSOSync.Persistence/Lock/DatabaseLockLease.cs`
- Create: `src/MSOSync.Persistence/Lock/DatabaseLockProvider.cs`
- Modify: `src/MSOSync.Persistence/PersistenceServiceExtensions.cs`

**Interfaces:**
- Consumes: `AppDbContext.Locks` (`DbSet<SyncLock>`, table `msosync.sync_lock`, columns `lock_name/lock_owner/lock_time`)
- Produces:
  - `IClock` — `DateTime UtcNow { get; }`
  - `SystemClock` — singleton impl
  - `LockNames.SyncEngine/RetryEngine/PurgeEngine` — string constants
  - `IDatabaseLockProvider.TryAcquireAsync(string lockName, CancellationToken)` → `DatabaseLockLease?`
  - `DatabaseLockLease` — `IAsyncDisposable`; releases lock on dispose
  - `AddPersistence()` now also registers `IDatabaseLockProvider → DatabaseLockProvider` (scoped)

---

- [ ] **Step 1: Create `IClock` and `SystemClock`**

```csharp
// src/MSOSync.Common/IClock.cs
namespace MSOSync.Common;

public interface IClock
{
    DateTime UtcNow { get; }
}
```

```csharp
// src/MSOSync.Common/SystemClock.cs
namespace MSOSync.Common;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

- [ ] **Step 2: Create `LockNames`**

```csharp
// src/MSOSync.Persistence/Lock/LockNames.cs
namespace MSOSync.Persistence.Lock;

public static class LockNames
{
    public const string SyncEngine  = "SYNC_ENGINE";
    public const string RetryEngine = "RETRY_ENGINE";
    public const string PurgeEngine = "PURGE_ENGINE";
}
```

- [ ] **Step 3: Create `IDatabaseLockProvider`**

```csharp
// src/MSOSync.Persistence/Lock/IDatabaseLockProvider.cs
namespace MSOSync.Persistence.Lock;

public interface IDatabaseLockProvider
{
    Task<DatabaseLockLease?> TryAcquireAsync(string lockName, CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `DatabaseLockLease`**

```csharp
// src/MSOSync.Persistence/Lock/DatabaseLockLease.cs
using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Lock;

public sealed class DatabaseLockLease : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly string _lockName;
    private readonly string _owner;
    private bool _disposed;

    internal DatabaseLockLease(AppDbContext db, string lockName, string owner)
    {
        _db = db;
        _lockName = lockName;
        _owner = owner;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _db.Database.ExecuteSqlRawAsync(
            $"UPDATE msosync.sync_lock SET lock_owner = NULL, lock_time = NULL " +
            $"WHERE lock_name = '{_lockName}' AND lock_owner = '{_owner}'");
    }
}
```

- [ ] **Step 5: Create `DatabaseLockProvider`**

```csharp
// src/MSOSync.Persistence/Lock/DatabaseLockProvider.cs
using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Lock;

public sealed class DatabaseLockProvider(AppDbContext db) : IDatabaseLockProvider
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public async Task<DatabaseLockLease?> TryAcquireAsync(string lockName, CancellationToken ct = default)
    {
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}";

        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            $"SET lock_owner = '{owner}', lock_time = GETUTCDATE() " +
            $"WHERE lock_name = '{lockName}' " +
            $"  AND (lock_owner IS NULL OR lock_time < DATEADD(MINUTE, -10, GETUTCDATE()))",
            ct);

        return rows == 1 ? new DatabaseLockLease(db, lockName, owner) : null;
    }
}
```

- [ ] **Step 6: Register `IDatabaseLockProvider` in `PersistenceServiceExtensions`**

Open `src/MSOSync.Persistence/PersistenceServiceExtensions.cs`. Add the using and registration:

```csharp
// add at top
using MSOSync.Persistence.Lock;
```

Inside `AddPersistence`, after the query registrations and before `AddHealthChecks`, add:

```csharp
        services.AddScoped<IDatabaseLockProvider, DatabaseLockProvider>();
```

Full file after edit:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence.Lock;
using MSOSync.Persistence.Queries;

namespace MSOSync.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required");

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", schema)));

        services.AddScoped<GetPendingBatchesQuery>();
        services.AddScoped<GetOfflineNodesQuery>();
        services.AddScoped<GetRetryCandidatesQuery>();
        services.AddScoped<GetEventQueueDepthQuery>();
        services.AddScoped<GetNodeByIdQuery>();
        services.AddScoped<GetNodeSecurityQuery>();
        services.AddScoped<GetUserByUsernameQuery>();

        services.AddScoped<IDatabaseLockProvider, DatabaseLockProvider>();

        services.AddHealthChecks()
            .AddCheck<PersistenceHealthCheck>("database");

        return services;
    }
}
```

- [ ] **Step 7: Build**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src/MSOSync.Common/MSOSync.Common.csproj -c Debug --warnaserror
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` for both.

- [ ] **Step 8: Commit**

```pwsh
git add src/MSOSync.Common/IClock.cs `
        src/MSOSync.Common/SystemClock.cs `
        src/MSOSync.Persistence/Lock/LockNames.cs `
        src/MSOSync.Persistence/Lock/IDatabaseLockProvider.cs `
        src/MSOSync.Persistence/Lock/DatabaseLockLease.cs `
        src/MSOSync.Persistence/Lock/DatabaseLockProvider.cs `
        src/MSOSync.Persistence/PersistenceServiceExtensions.cs
git commit -m "feat(common,persistence): add IClock, SystemClock, and DatabaseLockProvider"
```
