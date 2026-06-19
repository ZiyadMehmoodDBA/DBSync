# Epic 2 / Task 3: 23 Entity Configurations (Fluent API)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create one `IEntityTypeConfiguration<T>` per entity in `src/MSOSync.Persistence/Configurations/`. All column names, types, lengths, indexes, and constraints defined here.

**Architecture:** `ApplyConfigurationsFromAssembly` in `AppDbContext.OnModelCreating` auto-discovers all 23 configs. Each config reads schema from `MSOSYNC_SCHEMA` env var via a static field.

**Tech Stack:** EF Core 9.0.0 Fluent API

## Global Constraints

- Each config reads: `private static readonly string Schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";`
- `varchar` columns: `.HasColumnType("varchar(N)").HasMaxLength(N).IsUnicode(false)`
- `nvarchar(max)` columns: `.HasColumnType("nvarchar(max)")`
- `datetime2(7)` columns: `.HasColumnType("datetime2(7)")`
- IDENTITY bigint PKs: `.ValueGeneratedOnAdd()`
- Non-identity bigint PKs (SyncIncomingBatch, SyncDataEventBatch, SyncUserRole composite): `.ValueGeneratedNever()`
- `byte Status` columns: `.HasColumnType("tinyint").HasConversion<byte>()`
- `char EventType`: value converter — `HasConversion(v => v.ToString(), v => v.Length > 0 ? v[0] : 'I')`

---

**Files:** 23 files in `src/MSOSync.Persistence/Configurations/`

**Interfaces:**
- Consumes: all 23 entity types from Task 2
- Produces: configured EF model — consumed by Task 4 (AppDbContext), Task 5 (snapshot generation)

---

- [ ] **Step 1: Write all 23 configuration files**

```csharp
// src/MSOSync.Persistence/Configurations/SyncNodeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeConfiguration : IEntityTypeConfiguration<SyncNode>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNode> builder)
    {
        builder.ToTable("sync_node", Schema);
        builder.HasKey(e => e.NodeId);

        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.GroupId).HasColumnName("group_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.SyncUrl).HasColumnName("sync_url").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.RegistrationTime).HasColumnName("registration_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.LastHeartbeat).HasColumnName("last_heartbeat").HasColumnType("datetime2(7)");
        builder.Property(e => e.HeartbeatInterval).HasColumnName("heartbeat_interval").HasDefaultValue(60);
        builder.Property(e => e.SyncEnabled).HasColumnName("sync_enabled").HasDefaultValue(true);

        builder.HasIndex(e => e.LastHeartbeat).HasDatabaseName("IX_sync_node_last_heartbeat");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncNodeGroupConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeGroupConfiguration : IEntityTypeConfiguration<SyncNodeGroup>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeGroup> builder)
    {
        builder.ToTable("sync_node_group", Schema);
        builder.HasKey(e => e.GroupId);

        builder.Property(e => e.GroupId).HasColumnName("group_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.GroupName).HasColumnName("group_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncNodeSecurityConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeSecurityConfiguration : IEntityTypeConfiguration<SyncNodeSecurity>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeSecurity> builder)
    {
        builder.ToTable("sync_node_security", Schema);
        builder.HasKey(e => e.NodeId);

        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.NodeToken).HasColumnName("node_token").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false).IsRequired();
        builder.Property(e => e.CreatedTime).HasColumnName("created_time").HasColumnType("datetime2(7)");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncRegistrationRequestConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRegistrationRequestConfiguration : IEntityTypeConfiguration<SyncRegistrationRequest>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRegistrationRequest> builder)
    {
        builder.ToTable("sync_registration_request", Schema);
        builder.HasKey(e => e.RequestId);

        builder.Property(e => e.RequestId).HasColumnName("request_id").ValueGeneratedOnAdd();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.NodeGroup).HasColumnName("node_group").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.SyncUrl).HasColumnName("sync_url").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false);
        builder.Property(e => e.NodeVersion).HasColumnName("node_version").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.DbType).HasColumnName("db_type").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.RequestTime).HasColumnName("request_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.Approved).HasColumnName("approved").HasDefaultValue(false);
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncChannelConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncChannelConfiguration : IEntityTypeConfiguration<SyncChannel>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncChannel> builder)
    {
        builder.ToTable("sync_channel", Schema);
        builder.HasKey(e => e.ChannelId);

        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.Priority).HasColumnName("priority").IsRequired();
        builder.Property(e => e.BatchSize).HasColumnName("batch_size").HasDefaultValue(1000);
        builder.Property(e => e.MaxBatchToSend).HasColumnName("max_batch_to_send").HasDefaultValue(10);
        builder.Property(e => e.MaxDataSize).HasColumnName("max_data_size").HasDefaultValue(1048576L);
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncTriggerConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncTriggerConfiguration : IEntityTypeConfiguration<SyncTrigger>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncTrigger> builder)
    {
        builder.ToTable("sync_trigger", Schema);
        builder.HasKey(e => e.TriggerId);

        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.SourceTable).HasColumnName("source_table").HasColumnType("varchar(128)").HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.SyncOnInsert).HasColumnName("sync_on_insert").HasDefaultValue(true);
        builder.Property(e => e.SyncOnUpdate).HasColumnName("sync_on_update").HasDefaultValue(true);
        builder.Property(e => e.SyncOnDelete).HasColumnName("sync_on_delete").HasDefaultValue(true);
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
        builder.Property(e => e.TriggerVersion).HasColumnName("trigger_version").HasDefaultValue(0);
        builder.Property(e => e.LastVerifiedTime).HasColumnName("last_verified_time").HasColumnType("datetime2(7)");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncTriggerHistConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncTriggerHistConfiguration : IEntityTypeConfiguration<SyncTriggerHist>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncTriggerHist> builder)
    {
        builder.ToTable("sync_trigger_hist", Schema);
        builder.HasKey(e => e.HistId);

        builder.Property(e => e.HistId).HasColumnName("hist_id").ValueGeneratedOnAdd();
        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.DdlText).HasColumnName("ddl_text").HasColumnType("nvarchar(max)");
        builder.Property(e => e.TriggerVersion).HasColumnName("trigger_version");
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncRouterConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRouterConfiguration : IEntityTypeConfiguration<SyncRouter>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRouter> builder)
    {
        builder.ToTable("sync_router", Schema);
        builder.HasKey(e => e.RouterId);

        builder.Property(e => e.RouterId).HasColumnName("router_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.SourceNodeGroup).HasColumnName("source_node_group").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.TargetNodeGroup).HasColumnName("target_node_group").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.RouterType).HasColumnName("router_type").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).HasDefaultValue("default");
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncTriggerRouterConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncTriggerRouterConfiguration : IEntityTypeConfiguration<SyncTriggerRouter>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncTriggerRouter> builder)
    {
        builder.ToTable("sync_trigger_router", Schema);
        builder.HasKey(e => new { e.TriggerId, e.RouterId });

        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.RouterId).HasColumnName("router_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncDataEventConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncDataEventConfiguration : IEntityTypeConfiguration<SyncDataEvent>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncDataEvent> builder)
    {
        builder.ToTable("sync_data_event", Schema);
        builder.HasKey(e => e.EventId);

        builder.Property(e => e.EventId).HasColumnName("event_id").ValueGeneratedOnAdd();
        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.SourceNodeId).HasColumnName("source_node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasColumnType("char(1)")
            .IsUnicode(false)
            .HasConversion(v => v.ToString(), v => v.Length > 0 ? v[0] : 'I');
        builder.Property(e => e.TableName).HasColumnName("table_name").HasColumnType("varchar(128)").HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(e => e.PkData).HasColumnName("pk_data").HasColumnType("nvarchar(max)");
        builder.Property(e => e.RowData).HasColumnName("row_data").HasColumnType("nvarchar(max)");
        builder.Property(e => e.TransactionId).HasColumnName("transaction_id");
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)").IsRequired();
        builder.Property(e => e.IsProcessed).HasColumnName("is_processed").HasDefaultValue(false);

        builder.HasIndex(e => new { e.ChannelId, e.IsProcessed }).HasDatabaseName("IX_sync_data_event_channel_processed");
        builder.HasIndex(e => e.TransactionId).HasDatabaseName("IX_sync_data_event_transaction_id");
        builder.HasIndex(e => e.CreateTime).HasDatabaseName("IX_sync_data_event_create_time");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncDataEventBatchConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncDataEventBatchConfiguration : IEntityTypeConfiguration<SyncDataEventBatch>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncDataEventBatch> builder)
    {
        builder.ToTable("sync_data_event_batch", Schema);
        builder.HasKey(e => new { e.EventId, e.BatchId });

        builder.Property(e => e.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(e => e.BatchId).HasColumnName("batch_id").ValueGeneratedNever();
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncOutgoingBatchConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncOutgoingBatchConfiguration : IEntityTypeConfiguration<SyncOutgoingBatch>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncOutgoingBatch> builder)
    {
        builder.ToTable("sync_outgoing_batch", Schema);
        builder.HasKey(e => e.BatchId);

        builder.Property(e => e.BatchId).HasColumnName("batch_id").ValueGeneratedOnAdd();
        builder.Property(e => e.BatchSequence).HasColumnName("batch_sequence").IsRequired();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasColumnType("tinyint").HasConversion<byte>();
        builder.Property(e => e.RowCount).HasColumnName("row_count").HasDefaultValue(0);
        builder.Property(e => e.ByteCount).HasColumnName("byte_count").HasDefaultValue(0L);
        builder.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        builder.Property(e => e.NextRetryTime).HasColumnName("next_retry_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.NetworkMillis).HasColumnName("network_millis");
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.SentTime).HasColumnName("sent_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.AckTime).HasColumnName("ack_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => new { e.NodeId, e.Status }).HasDatabaseName("IX_sync_outgoing_batch_node_status");
        builder.HasIndex(e => e.NextRetryTime).HasDatabaseName("IX_sync_outgoing_batch_next_retry");
        builder.HasIndex(e => e.ChannelId).HasDatabaseName("IX_sync_outgoing_batch_channel");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncIncomingBatchConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncIncomingBatchConfiguration : IEntityTypeConfiguration<SyncIncomingBatch>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncIncomingBatch> builder)
    {
        builder.ToTable("sync_incoming_batch", Schema);
        builder.HasKey(e => e.BatchId);

        builder.Property(e => e.BatchId).HasColumnName("batch_id").ValueGeneratedNever();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasColumnType("tinyint").HasConversion<byte>();
        builder.Property(e => e.RowCount).HasColumnName("row_count");
        builder.Property(e => e.LoadTime).HasColumnName("load_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.ExtractTime).HasColumnName("extract_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.AppliedTime).HasColumnName("applied_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.ApplyTimeMs).HasColumnName("apply_time_ms");

        builder.HasIndex(e => new { e.NodeId, e.Status }).HasDatabaseName("IX_sync_incoming_batch_node_status");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncBatchErrorConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncBatchErrorConfiguration : IEntityTypeConfiguration<SyncBatchError>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncBatchError> builder)
    {
        builder.ToTable("sync_batch_error", Schema);
        builder.HasKey(e => e.ErrorId);

        builder.Property(e => e.ErrorId).HasColumnName("error_id").ValueGeneratedOnAdd();
        builder.Property(e => e.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(e => e.EventId).HasColumnName("event_id");
        builder.Property(e => e.ConflictType).HasColumnName("conflict_type").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("nvarchar(max)");
        builder.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        builder.Property(e => e.LastRetryTime).HasColumnName("last_retry_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => e.BatchId).HasDatabaseName("IX_sync_batch_error_batch_id");

        builder.HasOne<SyncOutgoingBatch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .HasConstraintName("FK_sync_batch_error_batch_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncMonitorConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncMonitorConfiguration : IEntityTypeConfiguration<SyncMonitor>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncMonitor> builder)
    {
        builder.ToTable("sync_monitor", Schema);
        builder.HasKey(e => e.SnapshotId);

        builder.Property(e => e.SnapshotId).HasColumnName("snapshot_id").ValueGeneratedOnAdd();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.MetricName).HasColumnName("metric_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.MetricValue).HasColumnName("metric_value").HasMaxLength(500);
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => new { e.NodeId, e.CreateTime }).HasDatabaseName("IX_sync_monitor_node_create_time");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncRuntimeStatsConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRuntimeStatsConfiguration : IEntityTypeConfiguration<SyncRuntimeStats>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRuntimeStats> builder)
    {
        builder.ToTable("sync_runtime_stats", Schema);
        builder.HasKey(e => e.StatId);

        builder.Property(e => e.StatId).HasColumnName("stat_id").ValueGeneratedOnAdd();
        builder.Property(e => e.HeapUsed).HasColumnName("heap_used");
        builder.Property(e => e.HeapMax).HasColumnName("heap_max");
        builder.Property(e => e.ThreadCount).HasColumnName("thread_count");
        builder.Property(e => e.CpuPercent).HasColumnName("cpu_percent").HasColumnType("decimal(5,2)");
        builder.Property(e => e.GcCount).HasColumnName("gc_count");
        builder.Property(e => e.GcTimeMs).HasColumnName("gc_time_ms");
        builder.Property(e => e.UptimeMs).HasColumnName("uptime_ms");
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncAuditConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncAuditConfiguration : IEntityTypeConfiguration<SyncAudit>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncAudit> builder)
    {
        builder.ToTable("sync_audit", Schema);
        builder.HasKey(e => e.AuditId);

        builder.Property(e => e.AuditId).HasColumnName("audit_id").ValueGeneratedOnAdd();
        builder.Property(e => e.Username).HasColumnName("username").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.ActionName).HasColumnName("action_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.ObjectName).HasColumnName("object_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => e.CreateTime).HasDatabaseName("IX_sync_audit_create_time");
        builder.HasIndex(e => e.Username).HasDatabaseName("IX_sync_audit_username");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncParameterConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncParameterConfiguration : IEntityTypeConfiguration<SyncParameter>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncParameter> builder)
    {
        builder.ToTable("sync_parameter", Schema);
        builder.HasKey(e => e.ParameterName);

        builder.Property(e => e.ParameterName).HasColumnName("parameter_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.ParameterValue).HasColumnName("parameter_value").HasColumnType("nvarchar(max)");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncParameterHistConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncParameterHistConfiguration : IEntityTypeConfiguration<SyncParameterHist>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncParameterHist> builder)
    {
        builder.ToTable("sync_parameter_hist", Schema);
        builder.HasKey(e => e.HistId);

        builder.Property(e => e.HistId).HasColumnName("hist_id").ValueGeneratedOnAdd();
        builder.Property(e => e.ParameterName).HasColumnName("parameter_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.OldValue).HasColumnName("old_value").HasColumnType("nvarchar(max)");
        builder.Property(e => e.NewValue).HasColumnName("new_value").HasColumnType("nvarchar(max)");
        builder.Property(e => e.ChangedBy).HasColumnName("changed_by").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.ChangeTime).HasColumnName("change_time").HasColumnType("datetime2(7)");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncLockConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncLockConfiguration : IEntityTypeConfiguration<SyncLock>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncLock> builder)
    {
        builder.ToTable("sync_lock", Schema);
        builder.HasKey(e => e.LockName);

        builder.Property(e => e.LockName).HasColumnName("lock_name").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.LockOwner).HasColumnName("lock_owner").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.LockTime).HasColumnName("lock_time").HasColumnType("datetime2(7)");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncUserConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserConfiguration : IEntityTypeConfiguration<SyncUser>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUser> builder)
    {
        builder.ToTable("sync_user", Schema);
        builder.HasKey(e => e.UserId);

        builder.Property(e => e.UserId).HasColumnName("user_id").ValueGeneratedOnAdd();
        builder.Property(e => e.Username).HasColumnName("username").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.PasswordHash).HasColumnName("password_hash").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false).IsRequired();
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
        builder.Property(e => e.LastLogin).HasColumnName("last_login").HasColumnType("datetime2(7)");
        builder.Property(e => e.FailedAttempts).HasColumnName("failed_attempts").HasDefaultValue(0);
        builder.Property(e => e.CreatedTime).HasColumnName("created_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => e.Username).IsUnique().HasDatabaseName("UQ_sync_user_username");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncRoleConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRoleConfiguration : IEntityTypeConfiguration<SyncRole>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRole> builder)
    {
        builder.ToTable("sync_role", Schema);
        builder.HasKey(e => e.RoleId);

        builder.Property(e => e.RoleId).HasColumnName("role_id").ValueGeneratedOnAdd();
        builder.Property(e => e.RoleName).HasColumnName("role_name").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();

        builder.HasIndex(e => e.RoleName).IsUnique().HasDatabaseName("UQ_sync_role_role_name");
    }
}
```

```csharp
// src/MSOSync.Persistence/Configurations/SyncUserRoleConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserRoleConfiguration : IEntityTypeConfiguration<SyncUserRole>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUserRole> builder)
    {
        builder.ToTable("sync_user_role", Schema);
        builder.HasKey(e => new { e.UserId, e.RoleId });

        builder.Property(e => e.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(e => e.RoleId).HasColumnName("role_id").ValueGeneratedNever();
    }
}
```

- [ ] **Step 2: Commit**

```powershell
git add src/MSOSync.Persistence/Configurations/
git commit -m "feat(persistence): add 23 Fluent API entity configurations"
```
