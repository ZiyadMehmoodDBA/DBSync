# Epic 2 / Task 2: 23 Entity Classes

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create all 23 sealed entity classes in `src/MSOSync.Persistence/Entities/`. No base class, no navigation properties, no inheritance.

**Architecture:** Plain POCO classes with auto-property syntax. Column mapping handled in Task 3 (configurations). Default values set on properties match the database defaults defined in configurations.

**Tech Stack:** C# 13 / .NET 9

## Global Constraints

- All entities are `sealed`
- No `virtual` properties (no lazy loading)
- No base class or shared interface
- No navigation properties
- `string` properties that are NOT NULL use `= null!` initializer
- `DateTime?` for nullable timestamps, `DateTime` for non-nullable
- dotnet PATH:
  ```powershell
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Create: `src/MSOSync.Persistence/Entities/SyncNode.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncNodeGroup.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncNodeSecurity.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncRegistrationRequest.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncChannel.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncTrigger.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncTriggerHist.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncRouter.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncTriggerRouter.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncDataEvent.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncDataEventBatch.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncOutgoingBatch.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncIncomingBatch.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncBatchError.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncMonitor.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncRuntimeStats.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncAudit.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncParameter.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncParameterHist.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncLock.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncUser.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncRole.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncUserRole.cs`
- Delete: `src/MSOSync.Persistence/Placeholder.cs`

**Interfaces:**
- Produces: all 23 entity types — consumed by Task 3 (configurations), Task 4 (AppDbContext), Task 7 (queries), Task 8 (tests)

---

- [ ] **Step 1: Write all 23 entity files**

```csharp
// src/MSOSync.Persistence/Entities/SyncNode.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncNode
{
    public string NodeId { get; set; } = null!;
    public string GroupId { get; set; } = null!;
    public string SyncUrl { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? RegistrationTime { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public int HeartbeatInterval { get; set; } = 60;
    public bool SyncEnabled { get; set; } = true;
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncNodeGroup.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeGroup
{
    public string GroupId { get; set; } = null!;
    public string? GroupName { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncNodeSecurity.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeSecurity
{
    public string NodeId { get; set; } = null!;
    public string NodeToken { get; set; } = null!;
    public DateTime? CreatedTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncRegistrationRequest.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncRegistrationRequest
{
    public long RequestId { get; set; }
    public string NodeId { get; set; } = null!;
    public string? NodeGroup { get; set; }
    public string? SyncUrl { get; set; }
    public string? NodeVersion { get; set; }
    public string? DbType { get; set; }
    public DateTime? RequestTime { get; set; }
    public bool Approved { get; set; } = false;
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncChannel.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncChannel
{
    public string ChannelId { get; set; } = null!;
    public int Priority { get; set; }
    public int BatchSize { get; set; } = 1000;
    public int MaxBatchToSend { get; set; } = 10;
    public long MaxDataSize { get; set; } = 1048576L;
    public bool Enabled { get; set; } = true;
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncTrigger.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncTrigger
{
    public string TriggerId { get; set; } = null!;
    public string SourceTable { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public bool SyncOnInsert { get; set; } = true;
    public bool SyncOnUpdate { get; set; } = true;
    public bool SyncOnDelete { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public int TriggerVersion { get; set; } = 0;
    public DateTime? LastVerifiedTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncTriggerHist.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncTriggerHist
{
    public long HistId { get; set; }
    public string TriggerId { get; set; } = null!;
    public string? DdlText { get; set; }
    public int? TriggerVersion { get; set; }
    public DateTime? CreateTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncRouter.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncRouter
{
    public string RouterId { get; set; } = null!;
    public string SourceNodeGroup { get; set; } = null!;
    public string TargetNodeGroup { get; set; } = null!;
    public string RouterType { get; set; } = "default";
    public bool Enabled { get; set; } = true;
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncTriggerRouter.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncTriggerRouter
{
    public string TriggerId { get; set; } = null!;
    public string RouterId { get; set; } = null!;
    public bool Enabled { get; set; } = true;
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncDataEvent.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncDataEvent
{
    public long EventId { get; set; }
    public string TriggerId { get; set; } = null!;
    public string SourceNodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public char EventType { get; set; }
    public string TableName { get; set; } = null!;
    public string? PkData { get; set; }
    public string? RowData { get; set; }
    public long? TransactionId { get; set; }
    public DateTime CreateTime { get; set; }
    public bool IsProcessed { get; set; } = false;
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncDataEventBatch.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncDataEventBatch
{
    public long EventId { get; set; }
    public long BatchId { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncOutgoingBatch.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncOutgoingBatch
{
    public long BatchId { get; set; }
    public long BatchSequence { get; set; }
    public string NodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public byte Status { get; set; }
    public int RowCount { get; set; } = 0;
    public long ByteCount { get; set; } = 0L;
    public int RetryCount { get; set; } = 0;
    public DateTime? NextRetryTime { get; set; }
    public long? NetworkMillis { get; set; }
    public DateTime? CreateTime { get; set; }
    public DateTime? SentTime { get; set; }
    public DateTime? AckTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncIncomingBatch.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncIncomingBatch
{
    public long BatchId { get; set; }
    public string NodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public byte Status { get; set; }
    public int? RowCount { get; set; }
    public DateTime? LoadTime { get; set; }
    public DateTime? ExtractTime { get; set; }
    public DateTime? AppliedTime { get; set; }
    public long? ApplyTimeMs { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncBatchError.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncBatchError
{
    public long ErrorId { get; set; }
    public long BatchId { get; set; }
    public long? EventId { get; set; }
    public string? ConflictType { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; } = 0;
    public DateTime? LastRetryTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncMonitor.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncMonitor
{
    public long SnapshotId { get; set; }
    public string? NodeId { get; set; }
    public string? MetricName { get; set; }
    public string? MetricValue { get; set; }
    public DateTime? CreateTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncRuntimeStats.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncRuntimeStats
{
    public long StatId { get; set; }
    public long? HeapUsed { get; set; }
    public long? HeapMax { get; set; }
    public int? ThreadCount { get; set; }
    public decimal? CpuPercent { get; set; }
    public long? GcCount { get; set; }
    public long? GcTimeMs { get; set; }
    public long? UptimeMs { get; set; }
    public DateTime? CreateTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncAudit.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncAudit
{
    public long AuditId { get; set; }
    public string? Username { get; set; }
    public string? ActionName { get; set; }
    public string? ObjectName { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime? CreateTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncParameter.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncParameter
{
    public string ParameterName { get; set; } = null!;
    public string? ParameterValue { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncParameterHist.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncParameterHist
{
    public long HistId { get; set; }
    public string ParameterName { get; set; } = null!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime? ChangeTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncLock.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncLock
{
    public string LockName { get; set; } = null!;
    public string? LockOwner { get; set; }
    public DateTime? LockTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncUser.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncUser
{
    public long UserId { get; set; }
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public DateTime? LastLogin { get; set; }
    public int FailedAttempts { get; set; } = 0;
    public DateTime? CreatedTime { get; set; }
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncRole.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncRole
{
    public long RoleId { get; set; }
    public string RoleName { get; set; } = null!;
}
```

```csharp
// src/MSOSync.Persistence/Entities/SyncUserRole.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncUserRole
{
    public long UserId { get; set; }
    public long RoleId { get; set; }
}
```

- [ ] **Step 2: Delete `Placeholder.cs`**

```powershell
Remove-Item src\MSOSync.Persistence\Placeholder.cs
```

- [ ] **Step 3: Verify build (entities compile; AppDbContext/configs not yet — partial compile OK via individual project)**

At this point `AppDbContextFactory.cs` references `AppDbContext` which doesn't exist yet. To verify entities alone, temporarily comment out `AppDbContextFactory.cs` or verify at Task 4. Skip full project build — proceed to Task 3.

- [ ] **Step 4: Commit**

```powershell
git add src/MSOSync.Persistence/Entities/
git rm src/MSOSync.Persistence/Placeholder.cs
git commit -m "feat(persistence): add 23 entity classes"
```
