# Task 2: MSOSync.Trigger

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `src/MSOSync.Trigger/TriggerDriftStatus.cs`
- Create: `src/MSOSync.Trigger/TriggerVerifyResult.cs`
- Create: `src/MSOSync.Trigger/SqlServerTriggerBuilder.cs`
- Create: `src/MSOSync.Trigger/ITriggerInstallationService.cs`
- Create: `src/MSOSync.Trigger/TriggerInstallationService.cs`
- Create: `src/MSOSync.Trigger/ITriggerDriftDetector.cs`
- Create: `src/MSOSync.Trigger/TriggerDriftDetector.cs`
- Create: `src/MSOSync.Trigger/TriggerServiceExtensions.cs`
- Delete: `src/MSOSync.Trigger/Placeholder.cs`

**Interfaces:**
- Consumes (from Task 1): nothing new — uses `AppDbContext` from Persistence
- Consumes: `SyncTrigger` (TriggerId, SourceTable, ChannelId, SyncOnInsert/Update/Delete, TriggerVersion), `SyncTriggerHist` (HistId, TriggerId, DdlText, TriggerVersion, CreateTime)
- Produces:
  - `TriggerDriftStatus` enum: `Valid, Drift, Missing`
  - `TriggerVerifyResult(string TriggerId, string NodeId, TriggerDriftStatus Status, int? InstalledVersion, int MetadataVersion, string? Message)`
  - `SqlServerTriggerBuilder.BuildDdl(SyncTrigger trigger, string nodeId)` → `string`
  - `ITriggerInstallationService`: `InstallAsync`, `DropAsync`, `RebuildAsync`
  - `ITriggerDriftDetector`: `DetectAllAsync`, `VerifyAsync`
  - `AddTriggerEngine(IServiceCollection, IConfiguration)` extension

---

- [ ] **Step 1: Create `TriggerDriftStatus`**

```csharp
// src/MSOSync.Trigger/TriggerDriftStatus.cs
using System.Text.Json.Serialization;

namespace MSOSync.Trigger;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerDriftStatus { Valid, Drift, Missing }
```

- [ ] **Step 2: Create `TriggerVerifyResult`**

```csharp
// src/MSOSync.Trigger/TriggerVerifyResult.cs
namespace MSOSync.Trigger;

public sealed record TriggerVerifyResult(
    string TriggerId,
    string NodeId,
    TriggerDriftStatus Status,
    int? InstalledVersion,
    int MetadataVersion,
    string? Message);
```

- [ ] **Step 3: Create `SqlServerTriggerBuilder`**

Trigger name convention: `msosync__{triggerId}` (always bracket-quoted in DDL).
Source table like `"dbo.Orders"` is split on first `.`.
Only active event types appear in `AFTER` clause.
`FOR JSON PATH` (array) handles multi-row DML safely.

```csharp
// src/MSOSync.Trigger/SqlServerTriggerBuilder.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Trigger;

public sealed class SqlServerTriggerBuilder
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public string BuildDdl(SyncTrigger trigger, string nodeId)
    {
        var triggerName = $"msosync__{trigger.TriggerId}";
        var parts = trigger.SourceTable.Split('.', 2);
        var tableSchema = parts.Length == 2 ? parts[0] : "dbo";
        var tableName   = parts.Length == 2 ? parts[1] : parts[0];

        var events = new List<string>();
        if (trigger.SyncOnInsert) events.Add("INSERT");
        if (trigger.SyncOnUpdate) events.Add("UPDATE");
        if (trigger.SyncOnDelete) events.Add("DELETE");
        var afterClause = string.Join(", ", events);

        return $"""
            CREATE OR ALTER TRIGGER [{triggerName}]
            ON [{tableSchema}].[{tableName}]
            AFTER {afterClause}
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @event_type CHAR(1) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted) THEN 'U'
                        WHEN EXISTS(SELECT 1 FROM inserted) THEN 'I'
                        ELSE 'D'
                    END;
                DECLARE @row_data NVARCHAR(MAX) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted)
                            THEN (SELECT * FROM inserted FOR JSON PATH)
                        ELSE (SELECT * FROM deleted FOR JSON PATH)
                    END;
                INSERT INTO [{Schema}].[sync_data_event]
                    (trigger_id, source_node_id, channel_id, event_type, table_name,
                     row_data, transaction_id, create_time, is_processed)
                VALUES (
                    '{trigger.TriggerId}',
                    N'{nodeId}',
                    '{trigger.ChannelId}',
                    @event_type,
                    '{trigger.SourceTable}',
                    @row_data,
                    CURRENT_TRANSACTION_ID(),
                    GETUTCDATE(),
                    0
                );
            END
            """;
    }

    public string GetTriggerName(string triggerId) => $"msosync__{triggerId}";
}
```

- [ ] **Step 4: Create `ITriggerInstallationService`**

```csharp
// src/MSOSync.Trigger/ITriggerInstallationService.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Trigger;

public interface ITriggerInstallationService
{
    Task<TriggerVerifyResult> InstallAsync(SyncTrigger trigger, string nodeId, CancellationToken ct = default);
    Task DropAsync(string triggerId, CancellationToken ct = default);
    Task<TriggerVerifyResult> RebuildAsync(string triggerId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create `TriggerInstallationService`**

```csharp
// src/MSOSync.Trigger/TriggerInstallationService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Trigger;

public sealed class TriggerInstallationService(
    AppDbContext db,
    SqlServerTriggerBuilder builder,
    IConfiguration config,
    ILogger<TriggerInstallationService> logger) : ITriggerInstallationService
{
    private string NodeId => config["Node:Id"] ?? Environment.MachineName;

    public async Task<TriggerVerifyResult> InstallAsync(
        SyncTrigger trigger, string nodeId, CancellationToken ct = default)
    {
        var ddl = builder.BuildDdl(trigger, nodeId);
        await db.Database.ExecuteSqlRawAsync(ddl, ct);

        trigger.TriggerVersion++;
        trigger.LastVerifiedTime = DateTime.UtcNow;
        db.TriggerHists.Add(new SyncTriggerHist
        {
            TriggerId = trigger.TriggerId,
            DdlText = ddl,
            TriggerVersion = trigger.TriggerVersion,
            CreateTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Trigger {TriggerId} installed v{Version}", trigger.TriggerId, trigger.TriggerVersion);
        return new TriggerVerifyResult(trigger.TriggerId, nodeId, TriggerDriftStatus.Valid,
            trigger.TriggerVersion, trigger.TriggerVersion, null);
    }

    public async Task DropAsync(string triggerId, CancellationToken ct = default)
    {
        var triggerName = builder.GetTriggerName(triggerId);
        await db.Database.ExecuteSqlRawAsync(
            $"IF OBJECT_ID(N'[{triggerName}]', N'TR') IS NOT NULL DROP TRIGGER [{triggerName}]", ct);
        logger.LogInformation("Trigger {TriggerId} dropped", triggerId);
    }

    public async Task<TriggerVerifyResult> RebuildAsync(string triggerId, CancellationToken ct = default)
    {
        var trigger = await db.Triggers.FirstOrDefaultAsync(t => t.TriggerId == triggerId, ct)
            ?? throw new NotFoundException($"Trigger '{triggerId}' not found", "TRIGGER_NOT_FOUND");
        return await InstallAsync(trigger, NodeId, ct);
    }
}
```

- [ ] **Step 6: Create `ITriggerDriftDetector`**

```csharp
// src/MSOSync.Trigger/ITriggerDriftDetector.cs
namespace MSOSync.Trigger;

public interface ITriggerDriftDetector
{
    Task DetectAllAsync(CancellationToken ct = default);
    Task<TriggerVerifyResult> VerifyAsync(string triggerId, CancellationToken ct = default);
}
```

- [ ] **Step 7: Create `TriggerDriftDetector`**

```csharp
// src/MSOSync.Trigger/TriggerDriftDetector.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Exceptions;
using MSOSync.Persistence;

namespace MSOSync.Trigger;

public sealed class TriggerDriftDetector(
    AppDbContext db,
    SqlServerTriggerBuilder builder,
    IConfiguration config,
    ILogger<TriggerDriftDetector> logger) : ITriggerDriftDetector
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    private string NodeId => config["Node:Id"] ?? Environment.MachineName;

    public async Task DetectAllAsync(CancellationToken ct = default)
    {
        var triggers = await db.Triggers.AsNoTracking()
            .Where(t => t.Enabled)
            .ToListAsync(ct);

        foreach (var t in triggers)
        {
            var result = await VerifyAsync(t.TriggerId, ct);
            if (result.Status != TriggerDriftStatus.Valid)
                logger.LogWarning("Trigger {TriggerId} status={Status}", t.TriggerId, result.Status);
        }
    }

    public async Task<TriggerVerifyResult> VerifyAsync(string triggerId, CancellationToken ct = default)
    {
        var trigger = await db.Triggers.FirstOrDefaultAsync(t => t.TriggerId == triggerId, ct)
            ?? throw new NotFoundException($"Trigger '{triggerId}' not found", "TRIGGER_NOT_FOUND");

        var nodeId = NodeId;
        var triggerName = builder.GetTriggerName(triggerId);

        // Query actual DDL from sys.sql_modules
        var installedDdl = await db.Database
            .SqlQueryRaw<string>(
                $"SELECT m.definition " +
                $"FROM sys.sql_modules m " +
                $"JOIN sys.triggers t ON t.object_id = m.object_id " +
                $"WHERE t.name = N'{triggerName}'")
            .FirstOrDefaultAsync(ct);

        if (installedDdl == null)
        {
            await UpdateLastVerified(trigger, ct);
            return new TriggerVerifyResult(triggerId, nodeId, TriggerDriftStatus.Missing,
                null, trigger.TriggerVersion, "Trigger not found in sys.triggers");
        }

        var expectedDdl = builder.BuildDdl(trigger, nodeId);
        var status = NormalizeDdl(installedDdl) == NormalizeDdl(expectedDdl)
            ? TriggerDriftStatus.Valid
            : TriggerDriftStatus.Drift;

        await UpdateLastVerified(trigger, ct);
        return new TriggerVerifyResult(triggerId, nodeId, status,
            trigger.TriggerVersion, trigger.TriggerVersion,
            status == TriggerDriftStatus.Drift ? "Installed DDL differs from expected" : null);
    }

    private async Task UpdateLastVerified(Persistence.Entities.SyncTrigger trigger, CancellationToken ct)
    {
        trigger.LastVerifiedTime = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeDdl(string ddl) =>
        string.Join(' ', ddl.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
              .ToUpperInvariant();
}
```

- [ ] **Step 8: Create `TriggerServiceExtensions`**

```csharp
// src/MSOSync.Trigger/TriggerServiceExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Trigger;

public static class TriggerServiceExtensions
{
    public static IServiceCollection AddTriggerEngine(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddSingleton<SqlServerTriggerBuilder>();
        services.AddScoped<ITriggerInstallationService, TriggerInstallationService>();
        services.AddScoped<ITriggerDriftDetector, TriggerDriftDetector>();
        return services;
    }
}
```

- [ ] **Step 9: Delete `Placeholder.cs`**

```pwsh
Remove-Item src/MSOSync.Trigger/Placeholder.cs
```

- [ ] **Step 10: Build**

```pwsh
dotnet build src/MSOSync.Trigger/MSOSync.Trigger.csproj -c Debug --warnaserror
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 11: Commit**

```pwsh
git add src/MSOSync.Trigger/TriggerDriftStatus.cs `
        src/MSOSync.Trigger/TriggerVerifyResult.cs `
        src/MSOSync.Trigger/SqlServerTriggerBuilder.cs `
        src/MSOSync.Trigger/ITriggerInstallationService.cs `
        src/MSOSync.Trigger/TriggerInstallationService.cs `
        src/MSOSync.Trigger/ITriggerDriftDetector.cs `
        src/MSOSync.Trigger/TriggerDriftDetector.cs `
        src/MSOSync.Trigger/TriggerServiceExtensions.cs
git rm src/MSOSync.Trigger/Placeholder.cs
git commit -m "feat(trigger): add SqlServerTriggerBuilder, installation service, and drift detector"
```
