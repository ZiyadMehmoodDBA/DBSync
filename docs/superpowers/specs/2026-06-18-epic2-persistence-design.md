# Epic 2: Persistence Foundation — Design Spec

**Date:** 2026-06-18
**Status:** Frozen
**Depends on:** Epic 1 (solution scaffold)
**Blocks:** Epic 3 (Security & Authentication)

---

## Goal

Implement `MSOSync.Persistence` completely: `AppDbContext`, 23 entities, Fluent API entity configurations, 8 EF Core migrations, seed data, 7 query objects, a health check, and integration tests. Running `dotnet ef database update` against a SQL Server 2022 instance creates the complete `msosync` schema with all tables, indexes, and seed data.

No authentication. No JWT. No middleware. No service layer. Persistence only.

---

## 1. Project Structure

```
src/MSOSync.Persistence/
├── AppDbContext.cs
├── PersistenceServiceExtensions.cs       AddPersistence() DI extension
├── PersistenceHealthCheck.cs
├── Entities/
│   ├── SyncNode.cs
│   ├── SyncNodeGroup.cs
│   ├── SyncNodeSecurity.cs
│   ├── SyncRegistrationRequest.cs
│   ├── SyncChannel.cs
│   ├── SyncTrigger.cs
│   ├── SyncTriggerHist.cs
│   ├── SyncRouter.cs
│   ├── SyncTriggerRouter.cs
│   ├── SyncDataEvent.cs
│   ├── SyncDataEventBatch.cs
│   ├── SyncOutgoingBatch.cs
│   ├── SyncIncomingBatch.cs
│   ├── SyncBatchError.cs
│   ├── SyncMonitor.cs
│   ├── SyncRuntimeStats.cs
│   ├── SyncAudit.cs
│   ├── SyncParameter.cs
│   ├── SyncParameterHist.cs
│   ├── SyncLock.cs
│   ├── SyncUser.cs
│   ├── SyncRole.cs
│   └── SyncUserRole.cs
├── Configurations/
│   ├── SyncNodeConfiguration.cs
│   ├── SyncNodeGroupConfiguration.cs
│   ├── SyncNodeSecurityConfiguration.cs
│   ├── SyncRegistrationRequestConfiguration.cs
│   ├── SyncChannelConfiguration.cs
│   ├── SyncTriggerConfiguration.cs
│   ├── SyncTriggerHistConfiguration.cs
│   ├── SyncRouterConfiguration.cs
│   ├── SyncTriggerRouterConfiguration.cs
│   ├── SyncDataEventConfiguration.cs
│   ├── SyncDataEventBatchConfiguration.cs
│   ├── SyncOutgoingBatchConfiguration.cs
│   ├── SyncIncomingBatchConfiguration.cs
│   ├── SyncBatchErrorConfiguration.cs
│   ├── SyncMonitorConfiguration.cs
│   ├── SyncRuntimeStatsConfiguration.cs
│   ├── SyncAuditConfiguration.cs
│   ├── SyncParameterConfiguration.cs
│   ├── SyncParameterHistConfiguration.cs
│   ├── SyncLockConfiguration.cs
│   ├── SyncUserConfiguration.cs
│   ├── SyncRoleConfiguration.cs
│   └── SyncUserRoleConfiguration.cs
├── Migrations/
│   ├── M001_CreateSchema.cs
│   ├── M002_CoreTables.cs
│   ├── M003_TriggerAndRoutingTables.cs
│   ├── M004_EventTables.cs
│   ├── M005_BatchTables.cs
│   ├── M006_MonitoringTables.cs
│   ├── M007_SecurityTables.cs
│   └── M008_SeedData.cs
└── Queries/
    ├── GetPendingBatchesQuery.cs
    ├── GetOfflineNodesQuery.cs
    ├── GetRetryCandidatesQuery.cs
    ├── GetEventQueueDepthQuery.cs
    ├── GetNodeByIdQuery.cs
    ├── GetNodeSecurityQuery.cs
    └── GetUserByUsernameQuery.cs
```

---

## 2. Entities (23, all `sealed`, no inheritance)

### Node & Group

```csharp
public sealed class SyncNode
{
    public string NodeId { get; set; } = null!;        // varchar(50) PK
    public string GroupId { get; set; } = null!;       // varchar(50) NOT NULL
    public string SyncUrl { get; set; } = null!;       // varchar(255) NOT NULL
    public string Status { get; set; } = null!;        // varchar(20): NEW|PENDING|APPROVED|REGISTERED|OFFLINE|DISABLED
    public DateTime? RegistrationTime { get; set; }    // datetime2(7)
    public DateTime? LastHeartbeat { get; set; }       // datetime2(7)
    public int HeartbeatInterval { get; set; } = 60;   // seconds
    public bool SyncEnabled { get; set; } = true;
}

public sealed class SyncNodeGroup
{
    public string GroupId { get; set; } = null!;       // varchar(50) PK
    public string? GroupName { get; set; }             // varchar(100)
}

public sealed class SyncNodeSecurity
{
    public string NodeId { get; set; } = null!;        // varchar(50) PK
    public string NodeToken { get; set; } = null!;     // varchar(255) BCrypt hash
    public DateTime? CreatedTime { get; set; }         // datetime2(7)
}

public sealed class SyncRegistrationRequest
{
    public long RequestId { get; set; }                // bigint IDENTITY PK
    public string NodeId { get; set; } = null!;        // varchar(50) NOT NULL
    public string? NodeGroup { get; set; }             // varchar(50)
    public string? SyncUrl { get; set; }               // varchar(255)
    public string? NodeVersion { get; set; }           // varchar(50)
    public string? DbType { get; set; }                // varchar(50)
    public DateTime? RequestTime { get; set; }         // datetime2(7)
    public bool Approved { get; set; } = false;
}
```

### Channel

```csharp
public sealed class SyncChannel
{
    public string ChannelId { get; set; } = null!;     // varchar(50) PK
    public int Priority { get; set; }                  // int NOT NULL
    public int BatchSize { get; set; } = 1000;
    public int MaxBatchToSend { get; set; } = 10;
    public long MaxDataSize { get; set; } = 1048576;   // bytes
    public bool Enabled { get; set; } = true;
}
```

### Trigger & Routing

```csharp
public sealed class SyncTrigger
{
    public string TriggerId { get; set; } = null!;     // varchar(50) PK
    public string SourceTable { get; set; } = null!;   // varchar(128) NOT NULL
    public string ChannelId { get; set; } = null!;     // varchar(50) NOT NULL
    public bool SyncOnInsert { get; set; } = true;
    public bool SyncOnUpdate { get; set; } = true;
    public bool SyncOnDelete { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public int TriggerVersion { get; set; } = 0;
    public DateTime? LastVerifiedTime { get; set; }    // datetime2(7)
}

public sealed class SyncTriggerHist
{
    public long HistId { get; set; }                   // bigint IDENTITY PK
    public string TriggerId { get; set; } = null!;     // varchar(50) NOT NULL
    public string? DdlText { get; set; }               // nvarchar(max)
    public int? TriggerVersion { get; set; }
    public DateTime? CreateTime { get; set; }          // datetime2(7)
}

public sealed class SyncRouter
{
    public string RouterId { get; set; } = null!;      // varchar(50) PK
    public string SourceNodeGroup { get; set; } = null!; // varchar(50) NOT NULL
    public string TargetNodeGroup { get; set; } = null!; // varchar(50) NOT NULL
    public string RouterType { get; set; } = "default"; // varchar(50)
    public bool Enabled { get; set; } = true;
}

public sealed class SyncTriggerRouter
{
    public string TriggerId { get; set; } = null!;     // varchar(50) PK (composite)
    public string RouterId { get; set; } = null!;      // varchar(50) PK (composite)
    public bool Enabled { get; set; } = true;
}
```

### Events

```csharp
public sealed class SyncDataEvent
{
    public long EventId { get; set; }                  // bigint IDENTITY PK
    public string TriggerId { get; set; } = null!;     // varchar(50) NOT NULL
    public string SourceNodeId { get; set; } = null!;  // varchar(50) NOT NULL
    public string ChannelId { get; set; } = null!;     // varchar(50) NOT NULL
    public char EventType { get; set; }                // char(1): I|U|D
    public string TableName { get; set; } = null!;     // varchar(128) NOT NULL
    public string? PkData { get; set; }                // nvarchar(max) FOR JSON PATH
    public string? RowData { get; set; }               // nvarchar(max) FOR JSON PATH
    public long? TransactionId { get; set; }           // bigint CURRENT_TRANSACTION_ID()
    public DateTime CreateTime { get; set; }           // datetime2(7) NOT NULL
    public bool IsProcessed { get; set; } = false;
}

public sealed class SyncDataEventBatch
{
    public long EventId { get; set; }                  // bigint PK (composite)
    public long BatchId { get; set; }                  // bigint PK (composite)
}
```

### Batches

```csharp
public sealed class SyncOutgoingBatch
{
    public long BatchId { get; set; }                  // bigint IDENTITY PK
    public long BatchSequence { get; set; }            // bigint NOT NULL
    public string NodeId { get; set; } = null!;        // varchar(50) NOT NULL
    public string ChannelId { get; set; } = null!;     // varchar(50) NOT NULL
    public byte Status { get; set; }                   // tinyint: 0=NEW,1=SENT,2=ACKED,3=ERROR,4=RETRY,5=OK,6=PARTIAL_SUCCESS
    public int RowCount { get; set; } = 0;
    public long ByteCount { get; set; } = 0;
    public int RetryCount { get; set; } = 0;
    public DateTime? NextRetryTime { get; set; }       // datetime2(7)
    public long? NetworkMillis { get; set; }
    public DateTime? CreateTime { get; set; }          // datetime2(7)
    public DateTime? SentTime { get; set; }            // datetime2(7)
    public DateTime? AckTime { get; set; }             // datetime2(7)
}

public sealed class SyncIncomingBatch
{
    public long BatchId { get; set; }                  // bigint PK (not identity)
    public string NodeId { get; set; } = null!;        // varchar(50) NOT NULL
    public string ChannelId { get; set; } = null!;     // varchar(50) NOT NULL
    public byte Status { get; set; }                   // tinyint
    public int? RowCount { get; set; }
    public DateTime? LoadTime { get; set; }            // datetime2(7)
    public DateTime? ExtractTime { get; set; }         // datetime2(7)
    public DateTime? AppliedTime { get; set; }         // datetime2(7)
    public long? ApplyTimeMs { get; set; }
}

public sealed class SyncBatchError
{
    public long ErrorId { get; set; }                  // bigint IDENTITY PK
    public long BatchId { get; set; }                  // bigint NOT NULL
    public long? EventId { get; set; }
    public string? ConflictType { get; set; }          // varchar(50): DUPLICATE_KEY|ROW_NOT_FOUND|FK_VIOLATION
    public string? ErrorMessage { get; set; }          // nvarchar(max)
    public int RetryCount { get; set; } = 0;
    public DateTime? LastRetryTime { get; set; }       // datetime2(7)
}
```

### Monitoring

```csharp
public sealed class SyncMonitor
{
    public long SnapshotId { get; set; }               // bigint IDENTITY PK
    public string? NodeId { get; set; }                // varchar(50)
    public string? MetricName { get; set; }            // varchar(100)
    public string? MetricValue { get; set; }           // nvarchar(500)
    public DateTime? CreateTime { get; set; }          // datetime2(7)
}

public sealed class SyncRuntimeStats
{
    public long StatId { get; set; }                   // bigint IDENTITY PK
    public long? HeapUsed { get; set; }
    public long? HeapMax { get; set; }
    public int? ThreadCount { get; set; }
    public decimal? CpuPercent { get; set; }           // decimal(5,2)
    public long? GcCount { get; set; }
    public long? GcTimeMs { get; set; }
    public long? UptimeMs { get; set; }
    public DateTime? CreateTime { get; set; }          // datetime2(7)
}

public sealed class SyncAudit
{
    public long AuditId { get; set; }                  // bigint IDENTITY PK
    public string? Username { get; set; }              // varchar(100)
    public string? ActionName { get; set; }            // varchar(100)
    public string? ObjectName { get; set; }            // varchar(100)
    public string? CorrelationId { get; set; }         // varchar(100)
    public DateTime? CreateTime { get; set; }          // datetime2(7)
}
```

### System

```csharp
public sealed class SyncParameter
{
    public string ParameterName { get; set; } = null!; // varchar(100) PK
    public string? ParameterValue { get; set; }        // nvarchar(max)
}

public sealed class SyncParameterHist
{
    public long HistId { get; set; }                   // bigint IDENTITY PK
    public string ParameterName { get; set; } = null!; // varchar(100) NOT NULL
    public string? OldValue { get; set; }              // nvarchar(max)
    public string? NewValue { get; set; }              // nvarchar(max)
    public string? ChangedBy { get; set; }             // varchar(100)
    public DateTime? ChangeTime { get; set; }          // datetime2(7)
}

public sealed class SyncLock
{
    public string LockName { get; set; } = null!;      // varchar(50) PK
    public string? LockOwner { get; set; }             // varchar(50)
    public DateTime? LockTime { get; set; }            // datetime2(7)
}
```

### Users

```csharp
public sealed class SyncUser
{
    public long UserId { get; set; }                   // bigint IDENTITY PK
    public string Username { get; set; } = null!;      // varchar(100) UNIQUE NOT NULL
    public string PasswordHash { get; set; } = null!;  // varchar(255) BCrypt
    public bool Enabled { get; set; } = true;
    public DateTime? LastLogin { get; set; }           // datetime2(7)
    public int FailedAttempts { get; set; } = 0;
    public DateTime? CreatedTime { get; set; }         // datetime2(7)
}

public sealed class SyncRole
{
    public long RoleId { get; set; }                   // bigint IDENTITY PK
    public string RoleName { get; set; } = null!;      // varchar(50) UNIQUE: ADMIN|OPERATOR|VIEWER
}

public sealed class SyncUserRole
{
    public long UserId { get; set; }                   // bigint PK (composite)
    public long RoleId { get; set; }                   // bigint PK (composite)
}
```

---

## 3. AppDbContext

```csharp
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SyncNode> Nodes => Set<SyncNode>();
    public DbSet<SyncNodeGroup> NodeGroups => Set<SyncNodeGroup>();
    public DbSet<SyncNodeSecurity> NodeSecurities => Set<SyncNodeSecurity>();
    public DbSet<SyncRegistrationRequest> RegistrationRequests => Set<SyncRegistrationRequest>();
    public DbSet<SyncChannel> Channels => Set<SyncChannel>();
    public DbSet<SyncTrigger> Triggers => Set<SyncTrigger>();
    public DbSet<SyncTriggerHist> TriggerHists => Set<SyncTriggerHist>();
    public DbSet<SyncRouter> Routers => Set<SyncRouter>();
    public DbSet<SyncTriggerRouter> TriggerRouters => Set<SyncTriggerRouter>();
    public DbSet<SyncDataEvent> DataEvents => Set<SyncDataEvent>();
    public DbSet<SyncDataEventBatch> DataEventBatches => Set<SyncDataEventBatch>();
    public DbSet<SyncOutgoingBatch> OutgoingBatches => Set<SyncOutgoingBatch>();
    public DbSet<SyncIncomingBatch> IncomingBatches => Set<SyncIncomingBatch>();
    public DbSet<SyncBatchError> BatchErrors => Set<SyncBatchError>();
    public DbSet<SyncMonitor> Monitors => Set<SyncMonitor>();
    public DbSet<SyncRuntimeStats> RuntimeStats => Set<SyncRuntimeStats>();
    public DbSet<SyncAudit> Audits => Set<SyncAudit>();
    public DbSet<SyncParameter> Parameters => Set<SyncParameter>();
    public DbSet<SyncParameterHist> ParameterHists => Set<SyncParameterHist>();
    public DbSet<SyncLock> Locks => Set<SyncLock>();
    public DbSet<SyncUser> Users => Set<SyncUser>();
    public DbSet<SyncRole> Roles => Set<SyncRole>();
    public DbSet<SyncUserRole> UserRoles => Set<SyncUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

Schema name injected via `PersistenceOptions.Schema` (read from env var `MSOSYNC_SCHEMA`, default `msosync`). Each configuration class receives the schema name via constructor.

---

## 4. Entity Configurations

One `IEntityTypeConfiguration<T>` per entity. Rules applied across all configurations:

- **Table name:** `builder.ToTable("sync_<name>", schema)`
- **All `DateTime` columns:** `.HasColumnType("datetime2(7)")`
- **All JSON columns** (`PkData`, `RowData`, `DdlText`, etc.): `.HasColumnType("nvarchar(max)")`
- **Enum columns** (`Status` on `SyncOutgoingBatch`/`SyncIncomingBatch`): `.HasConversion<byte>()`
- **String column lengths:** match the schema exactly (e.g., `varchar(50)` → `.HasMaxLength(50).IsUnicode(false)`)
- **No lazy loading** — virtual navigation properties are not used

### Indexes

Defined inline in configurations, not as separate migrations.

| Table | Indexes |
|---|---|
| `sync_data_event` | `(channel_id, is_processed)`, `(transaction_id)`, `(create_time)` |
| `sync_outgoing_batch` | `(node_id, status)`, `(next_retry_time)`, `(channel_id)` |
| `sync_incoming_batch` | `(node_id, status)` |
| `sync_node` | `(last_heartbeat)` |
| `sync_batch_error` | `(batch_id)` |
| `sync_parameter` | natural — PK is `parameter_name` |
| `sync_audit` | `(create_time)`, `(username)` |
| `sync_monitor` | `(node_id, create_time)` |

---

## 5. Migrations

8 EF Core code-first migrations. Each migration is idempotent when re-applied.

| # | Class name | Tables created |
|---|---|---|
| M001 | `CreateSchema` | creates `msosync` schema only |
| M002 | `CoreTables` | sync_node, sync_node_group, sync_node_security, sync_registration_request, sync_channel, sync_parameter, sync_parameter_hist, sync_lock |
| M003 | `TriggerAndRoutingTables` | sync_trigger, sync_trigger_hist, sync_router, sync_trigger_router |
| M004 | `EventTables` | sync_data_event, sync_data_event_batch |
| M005 | `BatchTables` | sync_outgoing_batch, sync_incoming_batch, sync_batch_error |
| M006 | `MonitoringTables` | sync_monitor, sync_runtime_stats, sync_audit |
| M007 | `SecurityTables` | sync_user, sync_role, sync_user_role |
| M008 | `SeedData` | inserts 3 roles, 1 default channel, 6 default parameters |

### Seed Data (M008)

**Roles:**
- `ADMIN` (role_id seeded as 1)
- `OPERATOR` (role_id seeded as 2)
- `VIEWER` (role_id seeded as 3)

**Default channel:**
- `channel_id=config`, `priority=100`, `batch_size=1000`, `max_batch_to_send=10`, `max_data_size=1048576`, `enabled=1`

**Default parameters:**

| parameter_name | value |
|---|---|
| `sync.interval.seconds` | `900` |
| `retention.days` | `30` |
| `audit.retention.days` | `90` |
| `max.retries` | `3` |
| `heartbeat.interval.seconds` | `60` |
| `queue.warn.threshold` | `0.8` |

Seed inserts use `IF NOT EXISTS` guards — running M008 twice produces no duplicates.

---

## 6. Query Objects

All query objects: constructor-injected `AppDbContext`, all queries use `.AsNoTracking()`.

```csharp
// GetPendingBatchesQuery — status IN (0=NEW, 4=RETRY) for a node+channel
public sealed class GetPendingBatchesQuery(AppDbContext db)
{
    public Task<List<SyncOutgoingBatch>> ExecuteAsync(
        string nodeId, string channelId, CancellationToken ct = default)
    => db.OutgoingBatches
        .AsNoTracking()
        .Where(b => b.NodeId == nodeId
                 && b.ChannelId == channelId
                 && (b.Status == 0 || b.Status == 4))
        .OrderBy(b => b.BatchSequence)
        .ToListAsync(ct);
}

// GetOfflineNodesQuery — last_heartbeat older than threshold minutes
public sealed class GetOfflineNodesQuery(AppDbContext db)
{
    public Task<List<SyncNode>> ExecuteAsync(
        int thresholdMinutes, CancellationToken ct = default)
    => db.Nodes
        .AsNoTracking()
        .Where(n => n.LastHeartbeat < DateTime.UtcNow.AddMinutes(-thresholdMinutes)
                 && n.Status == "REGISTERED")
        .ToListAsync(ct);
}

// GetRetryCandidatesQuery — ERROR batches eligible for retry
public sealed class GetRetryCandidatesQuery(AppDbContext db)
{
    public Task<List<SyncOutgoingBatch>> ExecuteAsync(
        int maxRetries, CancellationToken ct = default)
    => db.OutgoingBatches
        .AsNoTracking()
        .Where(b => b.Status == 3  // ERROR
                 && b.RetryCount < maxRetries
                 && b.NextRetryTime <= DateTime.UtcNow)
        .ToListAsync(ct);
}

// GetEventQueueDepthQuery — unprocessed event count per channel
public sealed class GetEventQueueDepthQuery(AppDbContext db)
{
    public Task<Dictionary<string, int>> ExecuteAsync(CancellationToken ct = default)
    => db.DataEvents
        .AsNoTracking()
        .Where(e => !e.IsProcessed)
        .GroupBy(e => e.ChannelId)
        .Select(g => new { g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}

// GetNodeByIdQuery
public sealed class GetNodeByIdQuery(AppDbContext db)
{
    public Task<SyncNode?> ExecuteAsync(string nodeId, CancellationToken ct = default)
    => db.Nodes
        .AsNoTracking()
        .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
}

// GetNodeSecurityQuery — used by Epic 3 node token validation
public sealed class GetNodeSecurityQuery(AppDbContext db)
{
    public Task<SyncNodeSecurity?> ExecuteAsync(string nodeId, CancellationToken ct = default)
    => db.NodeSecurities
        .AsNoTracking()
        .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
}

// GetUserByUsernameQuery — used by Epic 3 login path
public sealed class GetUserByUsernameQuery(AppDbContext db)
{
    public Task<SyncUser?> ExecuteAsync(string username, CancellationToken ct = default)
    => db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(u => u.Username == username, ct);
}
```

---

## 7. DI Registration

```csharp
// PersistenceServiceExtensions.cs
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

        services.AddHealthChecks()
            .AddCheck<PersistenceHealthCheck>("database");

        return services;
    }
}
```

---

## 8. Health Check

```csharp
public sealed class PersistenceHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await db.Database.CanConnectAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable", ex);
        }
    }
}
```

Wired into `/api/v1/health/ready` in Epic 3+.

---

## 9. Integration Tests

All tests in `MSOSync.IntegrationTests`. Container: `Testcontainers.MsSql` — spins up SQL Server 2022 per test class, runs `MigrateAsync()` once, disposes after.

### Test Fixture

```csharp
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    public AppDbContext Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;
        Db = new AppDbContext(opts);
        await Db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await _container.DisposeAsync();
    }
}
```

### Tests

| Test | Assertion |
|---|---|
| `CanConnect` | `CanConnectAsync()` returns true |
| `SchemaCreated_All23TablesExist` | Query `INFORMATION_SCHEMA.TABLES` — exactly 23 tables in `msosync` schema |
| `SeedData_RolesPresent` | ADMIN, OPERATOR, VIEWER roles exist (count=3) |
| `SeedData_DefaultChannelPresent` | channel_id=`config`, priority=100 |
| `SeedData_ParametersPresent` | 6 parameters with correct values |
| `MigrationIdempotency` | Call `MigrateAsync()` twice — no exception, role count still 3 |
| `ForeignKeyIntegrity` | Insert `SyncBatchError` with non-existent `BatchId` — SQL Server throws |
| `QueryObjects_GetOfflineNodes` | Insert node with old heartbeat, verify returned |
| `QueryObjects_GetPendingBatches` | Insert NEW batch, verify returned; insert ACKED batch, verify excluded |
| `QueryObjects_GetRetryCandidates` | Insert ERROR batch with past retry time, verify returned |
| `QueryObjects_GetEventQueueDepth` | Insert 3 unprocessed events on channel A, 1 on channel B, verify counts |

---

## 10. Constraints & Rules

- **No `datetime`** — all timestamp columns use `datetime2(7)` explicitly
- **No implicit enum storage** — `byte` status columns use `.HasConversion<byte>()` in configuration
- **No lazy loading** — no `virtual` navigation properties
- **No repository interfaces** — domain services inject `AppDbContext` directly
- **`AsNoTracking()`** — mandatory on all query objects
- **Schema name** — always from `MSOSYNC_SCHEMA` env var, never hardcoded
- **Seed idempotency** — M008 uses `IF NOT EXISTS` guards for all inserts
- **Migrations run in order** — M001 → M008; no gaps, no out-of-order applies

---

## Out of Scope

- ASP.NET Core Identity tables (using custom sync_user/sync_role tables instead)
- JWT / authentication middleware
- Service layer (NodeService, BatchService, etc.)
- Repository interfaces or abstractions
- OpenTelemetry instrumentation (Epic 9)
