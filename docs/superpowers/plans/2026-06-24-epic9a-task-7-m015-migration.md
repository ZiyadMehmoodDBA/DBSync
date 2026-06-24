# Task 7: M015 Migration + AddMetadata() DI Wiring

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Add new indexes to entity configurations, scaffold M015 migration, replace the auto-generated `AddColumn` with safe nullable→backfill→NOT NULL SQL, and register all new services + validators in `AddMetadata()`.

**Key facts:**
- `SyncBatchError.CreateTime` was added to the entity and config in Task 5.
- `IX_sync_data_event_create_time` **already exists** in `SyncDataEventConfiguration` — do NOT add it again.
- `IX_sync_data_event_batch_event_id` — NOT needed. PK on `(event_id, batch_id)` covers event_id lookups.
- `IX_sync_batch_error_batch_id` — already exists from M005. Do NOT add it again.
- `ViewerOrAbove` policy already exists. No change to SecurityServiceExtensions.
- `dotnet ef` path: `C:\Users\zmehmood\.dotnet\dotnet.exe`

**Files:**
- Modify: `src/MSOSync.Persistence/Configurations/SyncDataEventConfiguration.cs` — add 3 indexes
- Modify: `src/MSOSync.Persistence/Configurations/SyncIncomingBatchConfiguration.cs` — add 3 indexes
- Create: `src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.cs` (scaffolded then edited)
- Create: `src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.Designer.cs` (auto-generated)
- Modify: `src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs` (auto-updated)
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs` — add DI registrations

---

- [ ] **Step 1: Add new indexes to SyncDataEventConfiguration**

Open `src/MSOSync.Persistence/Configurations/SyncDataEventConfiguration.cs`.

Current `Configure` method ends with:
```csharp
builder.HasIndex(e => new { e.ChannelId, e.IsProcessed }).HasDatabaseName("IX_sync_data_event_channel_processed");
builder.HasIndex(e => e.TransactionId).HasDatabaseName("IX_sync_data_event_transaction_id");
builder.HasIndex(e => e.CreateTime).HasDatabaseName("IX_sync_data_event_create_time");
```

Add three new indexes **after** the existing ones:
```csharp
builder.HasIndex(e => e.SourceNodeId).HasDatabaseName("IX_sync_data_event_source_node_id");
builder.HasIndex(e => e.TriggerId).HasDatabaseName("IX_sync_data_event_trigger_id");
builder.HasIndex(e => new { e.ChannelId, e.CreateTime })
    .IsDescending(false, true)
    .HasDatabaseName("IX_sync_data_event_channel_time");
```

- [ ] **Step 2: Add new indexes to SyncIncomingBatchConfiguration**

Open `src/MSOSync.Persistence/Configurations/SyncIncomingBatchConfiguration.cs`.

Current `Configure` method ends with:
```csharp
builder.HasIndex(e => new { e.NodeId, e.Status }).HasDatabaseName("IX_sync_incoming_batch_node_status");
builder.HasIndex(e => new { e.SourceNodeId, e.ChannelId, e.BatchSequence })
    .HasDatabaseName("IX_sync_incoming_batch_source_channel_sequence");
```

Add three new indexes **after** the existing ones:
```csharp
builder.HasIndex(e => e.ReceivedTime)
    .IsDescending(true)
    .HasDatabaseName("IX_sync_incoming_batch_received_time");
builder.HasIndex(e => new { e.SourceNodeId, e.ReceivedTime })
    .IsDescending(false, true)
    .HasDatabaseName("IX_sync_incoming_batch_source_node_time");
builder.HasIndex(e => new { e.Status, e.ReceivedTime })
    .IsDescending(false, true)
    .HasDatabaseName("IX_sync_incoming_batch_status_time");
```

- [ ] **Step 3: Verify EF Core can detect the model changes**

```powershell
$env:DOTNET_ROOT  = "C:\Users\zmehmood\.dotnet"
$env:PATH         = "C:\Users\zmehmood\.dotnet;$env:PATH"
$env:MSOSYNC_JWT_SECRET = "test-jwt-secret-value-at-least-32-chars!"
dotnet build src\MSOSync.Persistence -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Scaffold M015 migration**

```powershell
$env:DOTNET_ROOT  = "C:\Users\zmehmood\.dotnet"
$env:PATH         = "C:\Users\zmehmood\.dotnet;$env:PATH"
$env:MSOSYNC_JWT_SECRET = "test-jwt-secret-value-at-least-32-chars!"
dotnet ef migrations add M015_OperationalReadAPIs `
    --project src\MSOSync.Persistence `
    --startup-project src\MSOSync.App `
    --output-dir Migrations
```

This generates:
- `src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.cs`
- `src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.Designer.cs`
- Updates `AppDbContextModelSnapshot.cs`

- [ ] **Step 5: Replace auto-generated Up() with safe create_time migration**

Open the generated `src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.cs`.

The auto-generated `Up()` will have something like:
```csharp
migrationBuilder.AddColumn<DateTime>(
    name: "create_time",
    schema: "msosync",
    table: "sync_batch_error",
    type: "datetime2(7)",
    nullable: false,
    defaultValueSql: "SYSUTCDATETIME()");
```

**Problem:** `AddColumn` with `nullable: false` fails if existing rows have no value. Replace the entire `Up()` method body with:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Add create_time to sync_batch_error (nullable → backfill → NOT NULL + default)
    migrationBuilder.Sql("""
        ALTER TABLE [msosync].[sync_batch_error] ADD [create_time] datetime2(7) NULL;
        """);
    migrationBuilder.Sql("""
        UPDATE [msosync].[sync_batch_error] SET [create_time] = SYSUTCDATETIME() WHERE [create_time] IS NULL;
        """);
    migrationBuilder.Sql("""
        ALTER TABLE [msosync].[sync_batch_error] ALTER COLUMN [create_time] datetime2(7) NOT NULL;
        """);
    migrationBuilder.Sql("""
        ALTER TABLE [msosync].[sync_batch_error]
            ADD CONSTRAINT [DF_sync_batch_error_create_time] DEFAULT SYSUTCDATETIME() FOR [create_time];
        """);

    // Indexes on sync_data_event (IX_sync_data_event_create_time already exists — skip)
    migrationBuilder.CreateIndex(
        name:   "IX_sync_data_event_source_node_id",
        schema: "msosync",
        table:  "sync_data_event",
        column: "source_node_id");

    migrationBuilder.CreateIndex(
        name:   "IX_sync_data_event_trigger_id",
        schema: "msosync",
        table:  "sync_data_event",
        column: "trigger_id");

    migrationBuilder.Sql("""
        CREATE INDEX [IX_sync_data_event_channel_time]
            ON [msosync].[sync_data_event] ([channel_id] ASC, [create_time] DESC);
        """);

    // Indexes on sync_incoming_batch
    migrationBuilder.Sql("""
        CREATE INDEX [IX_sync_incoming_batch_received_time]
            ON [msosync].[sync_incoming_batch] ([received_time] DESC);
        """);

    migrationBuilder.CreateIndex(
        name:    "IX_sync_incoming_batch_source_node_time",
        schema:  "msosync",
        table:   "sync_incoming_batch",
        columns: new[] { "source_node_id", "received_time" });

    migrationBuilder.CreateIndex(
        name:    "IX_sync_incoming_batch_status_time",
        schema:  "msosync",
        table:   "sync_incoming_batch",
        columns: new[] { "status", "received_time" });

    // Index on sync_batch_error
    // IX_sync_batch_error_batch_id already exists from M005 — skip
    migrationBuilder.Sql("""
        CREATE INDEX [IX_sync_batch_error_conflict_create]
            ON [msosync].[sync_batch_error] ([conflict_type] ASC, [create_time] DESC);
        """);
}
```

- [ ] **Step 6: Replace auto-generated Down() with safe rollback**

Replace the `Down()` method body with:

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_batch_error_conflict_create] ON [msosync].[sync_batch_error];");
    migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_incoming_batch_status_time] ON [msosync].[sync_incoming_batch];");
    migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_incoming_batch_source_node_time] ON [msosync].[sync_incoming_batch];");
    migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_incoming_batch_received_time] ON [msosync].[sync_incoming_batch];");
    migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_data_event_channel_time] ON [msosync].[sync_data_event];");
    migrationBuilder.DropIndex(name: "IX_sync_data_event_trigger_id",    schema: "msosync", table: "sync_data_event");
    migrationBuilder.DropIndex(name: "IX_sync_data_event_source_node_id", schema: "msosync", table: "sync_data_event");
    migrationBuilder.Sql("ALTER TABLE [msosync].[sync_batch_error] DROP CONSTRAINT [DF_sync_batch_error_create_time];");
    migrationBuilder.DropColumn(name: "create_time", schema: "msosync", table: "sync_batch_error");
}
```

- [ ] **Step 7: Verify the migration file compiles**

```powershell
dotnet build src\MSOSync.Persistence -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 8: Apply migration to LocalDB (optional but recommended)**

```powershell
$env:DOTNET_ROOT  = "C:\Users\zmehmood\.dotnet"
$env:PATH         = "C:\Users\zmehmood\.dotnet;$env:PATH"
$env:MSOSYNC_JWT_SECRET = "test-jwt-secret-value-at-least-32-chars!"
dotnet ef database update `
    --project src\MSOSync.Persistence `
    --startup-project src\MSOSync.App
```

Expected: `Done.` — migration M015 applied successfully.

- [ ] **Step 9: Register services in AddMetadata()**

Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`.

Add required using statements at top:

```csharp
using FluentValidation;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.IncomingBatches;
```

Replace the `AddMetadata` method body:

```csharp
public static IServiceCollection AddMetadata(
    this IServiceCollection services,
    IConfiguration _)
{
    services.AddMemoryCache();
    services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssemblyContaining<ParameterMetadataService>());

    // Existing services
    services.AddScoped<IParameterMetadataService, ParameterMetadataService>();
    services.AddScoped<INodeMetadataService, NodeMetadataService>();
    services.AddScoped<ITriggerMetadataService, TriggerMetadataService>();
    services.AddScoped<IRouterMetadataService, RouterMetadataService>();
    services.AddScoped<IChannelMetadataService, ChannelMetadataService>();
    services.AddScoped<INodeStateMachine, NodeStateMachine>();
    services.AddScoped<IUsersManagementService, UsersManagementService>();

    // Epic 9A — Operational Read APIs
    services.AddSingleton<IErrorSeverityClassifier, ErrorSeverityClassifier>();
    services.AddScoped<IEventQueryService, EventQueryService>();
    services.AddScoped<IIncomingBatchQueryService, IncomingBatchQueryService>();
    services.AddScoped<IBatchErrorQueryService, BatchErrorQueryService>();
    services.AddScoped<IValidator<EventFilter>, EventFilterValidator>();
    services.AddScoped<IValidator<IncomingBatchFilter>, IncomingBatchFilterValidator>();
    services.AddScoped<IValidator<BatchErrorFilter>, BatchErrorFilterValidator>();

    return services;
}
```

- [ ] **Step 10: Verify full solution builds**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 11: Commit**

```powershell
git add src/MSOSync.Persistence/Configurations/SyncDataEventConfiguration.cs
git add src/MSOSync.Persistence/Configurations/SyncIncomingBatchConfiguration.cs
git add src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.cs
git add src/MSOSync.Persistence/Migrations/M015_OperationalReadAPIs.Designer.cs
git add src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs
git add src/MSOSync.Metadata/MetadataServiceExtensions.cs
git commit -m "feat(9a): M015 migration (create_time + 6 indexes); wire query services and validators in AddMetadata"
```
