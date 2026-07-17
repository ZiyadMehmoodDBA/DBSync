# Task 2: EF Configuration Updates — tenant_id Column + Composite Index

**Part of:** [Epic 15B Domain Tenant Migration](2026-07-17-epic15b-domain-tenant-migration.md)

**Goal:** Add `tenant_id` column configuration and composite index to each of the 21 entity EF configuration classes. After this task, EF Core knows about the column and index — the actual DB schema change happens in Task 3.

**Files to modify** (all in `src/MSOSync.Persistence/Configurations/`):
- `SyncRegistrationRequestConfiguration.cs`, `SyncNodeBootstrapTokenConfiguration.cs`, `SyncNodeLifecycleHistoryConfiguration.cs`, `SyncNodeConnectivityHistoryConfiguration.cs`
- `SyncDataEventConfiguration.cs`, `SyncDataEventBatchConfiguration.cs`, `SyncOutgoingBatchConfiguration.cs`, `SyncIncomingBatchConfiguration.cs`, `SyncBatchErrorConfiguration.cs`
- `SyncConfigurationTemplateConfiguration.cs`, `SyncConfigurationTemplateVersionConfiguration.cs`, `SyncNodeConfigurationOverrideConfiguration.cs`, `SyncNodeConfigurationHistoryConfiguration.cs`, `SyncConfigurationRolloutConfiguration.cs`
- `SyncRuntimeStatsConfiguration.cs`, `SyncAuditConfiguration.cs`, `SyncOperationConfiguration.cs`, `SyncExportJobConfiguration.cs`
- `SyncNotificationConfiguration.cs`, `SyncUserNotificationConfiguration.cs`, `SyncUserRefreshTokenConfiguration.cs`

**Interfaces:**
- Consumes: `TenantId` property on each entity from Task 1
- Produces: EF model with `tenant_id` columns + composite indexes — consumed by `AppDbContext.ApplyTenantFilters` (from 15A) and by Task 3's migration

---

## Pattern

Inside each configuration class's `Configure(EntityTypeBuilder<T> builder)` method, add these two blocks:

**Composite index variant** (used by 19 of 21 entities):
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.SECOND_PROPERTY })
    .HasDatabaseName("IX_TABLE_TenantId_SECOND");
```

**Single-column index variant** (used by `SyncConfigurationTemplate` only):
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => e.TenantId)
    .HasDatabaseName("IX_sync_configuration_template_TenantId");
```

**`SyncConfigurationTemplate` unique constraint change** — additionally, find the existing unique index and change it:

Find:
```csharp
builder.HasIndex(e => e.Name)
    .IsUnique()
    .HasDatabaseName("UX_sync_configuration_template_name");
```

Replace with:
```csharp
builder.HasIndex(e => new { e.TenantId, e.Name })
    .IsUnique()
    .HasDatabaseName("UX_sync_configuration_template_tenant_id_name");
```

---

## Per-Entity Configuration

Apply to each file in order. The **second property** for each composite index uses the C# property name on the entity.

### Group 1 — Node Management

**`SyncRegistrationRequestConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.Status })
    .HasDatabaseName("IX_sync_registration_request_TenantId_Status");
```

**`SyncNodeBootstrapTokenConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.NodeId })
    .HasDatabaseName("IX_sync_node_bootstrap_token_TenantId_NodeId");
```

**`SyncNodeLifecycleHistoryConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.NodeId })
    .HasDatabaseName("IX_sync_node_lifecycle_history_TenantId_NodeId");
```

**`SyncNodeConnectivityHistoryConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.NodeId })
    .HasDatabaseName("IX_sync_node_connectivity_history_TenantId_NodeId");
```

### Group 2 — Synchronization Engine

**`SyncDataEventConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.CreateTime })
    .HasDatabaseName("IX_sync_data_event_TenantId_CreateTime");
```

**`SyncDataEventBatchConfiguration.cs`**

> `SyncDataEventBatch` is a junction table (EventId + BatchId). Use `BatchId` as the second column.

```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.BatchId })
    .HasDatabaseName("IX_sync_data_event_batch_TenantId_BatchId");
```

**`SyncOutgoingBatchConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.Status })
    .HasDatabaseName("IX_sync_outgoing_batch_TenantId_Status");
```

**`SyncIncomingBatchConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.Status })
    .HasDatabaseName("IX_sync_incoming_batch_TenantId_Status");
```

**`SyncBatchErrorConfiguration.cs`**

> Verify the C# property name for the creation timestamp in `SyncBatchError.cs`. It is `CreateTime` based on other entities' conventions. If the actual property differs, use the correct name.

```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.CreateTime })
    .HasDatabaseName("IX_sync_batch_error_TenantId_CreateTime");
```

### Group 3 — Configuration Management

**`SyncConfigurationTemplateConfiguration.cs`**

Two changes: (1) add `tenant_id` column, (2) convert unique Name index to composite (TenantId, Name).

```csharp
// Add TenantId column
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

// Single TenantId index for general lookups
builder.HasIndex(e => e.TenantId)
    .HasDatabaseName("IX_sync_configuration_template_TenantId");
```

And REPLACE the existing unique Name index — find:
```csharp
builder.HasIndex(e => e.Name)
    .IsUnique()
    .HasDatabaseName("UX_sync_configuration_template_name");
```
Replace with:
```csharp
builder.HasIndex(e => new { e.TenantId, e.Name })
    .IsUnique()
    .HasDatabaseName("UX_sync_configuration_template_tenant_id_name");
```

**`SyncConfigurationTemplateVersionConfiguration.cs`**

> `TemplateId` is the FK property to the parent template. Check the actual C# property name in `SyncConfigurationTemplateVersion.cs` — it is likely `TemplateId`. Use `TemplateId` below.

```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.TemplateId })
    .HasDatabaseName("IX_sync_configuration_template_version_TenantId_TemplateId");
```

**`SyncNodeConfigurationOverrideConfiguration.cs`**

> `NodeId` is the string FK property. Verify the C# property name is `NodeId` in `SyncNodeConfigurationOverride.cs`.

```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.NodeId })
    .HasDatabaseName("IX_sync_node_configuration_override_TenantId_NodeId");
```

**`SyncNodeConfigurationHistoryConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.NodeId })
    .HasDatabaseName("IX_sync_node_configuration_history_TenantId_NodeId");
```

**`SyncConfigurationRolloutConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.Status })
    .HasDatabaseName("IX_sync_configuration_rollout_TenantId_Status");
```

### Group 4 — Operations & Audit

**`SyncRuntimeStatsConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.CreateTime })
    .HasDatabaseName("IX_sync_runtime_stats_TenantId_CreateTime");
```

**`SyncAuditConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.CreateTime })
    .HasDatabaseName("IX_sync_audit_TenantId_CreateTime");
```

**`SyncOperationConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.Status })
    .HasDatabaseName("IX_sync_operation_TenantId_Status");
```

**`SyncExportJobConfiguration.cs`**

> `SyncExportJob` uses `dbo` schema (no explicit schema in `ToTable`). The `tenant_id` column and index are still added the same way — EF handles the schema difference.

```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.Status })
    .HasDatabaseName("IX_sync_export_job_TenantId_Status");
```

### Group 5 — User & Runtime

**`SyncNotificationConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.CreateTime })
    .HasDatabaseName("IX_sync_notification_TenantId_CreateTime");
```

> Verify `SyncNotification` has a `CreateTime` property. If the timestamp property has a different name (e.g., `CreatedAt`), use the actual name and adjust the DB index name accordingly.

**`SyncUserNotificationConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.UserId })
    .HasDatabaseName("IX_sync_user_notification_TenantId_UserId");
```

**`SyncUserRefreshTokenConfiguration.cs`**
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.UserId })
    .HasDatabaseName("IX_sync_user_refresh_token_TenantId_UserId");
```

---

- [ ] **Step 1: Apply all 21 configuration changes**

Work through the per-entity sections above in order. For each file, add the `Property` + `HasIndex` block inside the `Configure()` method. For `SyncConfigurationTemplate`, also replace the existing unique Name index.

- [ ] **Step 2: Build persistence project to verify no EF model errors**

```
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj
```

Expected: `Build succeeded. 0 Error(s) 0 Warning(s)`

If you see `CS1061: 'EntityTypeBuilder<SyncXxx>' does not contain a definition for 'HasIndex'`, the using for `Microsoft.EntityFrameworkCore` is missing. If you see property-not-found errors, verify the C# property name against the entity class.

- [ ] **Step 3: Run all unit tests**

```
dotnet test MSOSync.sln --filter "Category!=Integration" -v minimal
```

Expected: all existing tests pass.

- [ ] **Step 4: Commit**

```
git add src/MSOSync.Persistence/Configurations/
git commit -m "feat(15B-2): EF config tenant_id column + composite indexes on 21 entities"
```
