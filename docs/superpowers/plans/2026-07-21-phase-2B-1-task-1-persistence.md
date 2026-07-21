# 2B.1 Task 1 — Persistence + M033

**Files:**
- Modify: `src/MSOSync.Persistence/Entities/NodeLifecycleState.cs`
- Modify: `src/MSOSync.Persistence/Entities/SyncNode.cs`
- Modify: `src/MSOSync.Persistence/Entities/SyncOperation.cs` (comment only)
- Create: `src/MSOSync.Persistence/Entities/SyncOperationStep.cs`
- Create: `src/MSOSync.Persistence/Configurations/SyncOperationStepConfiguration.cs`
- Modify: `src/MSOSync.Persistence/AppDbContext.cs` (DbSet)
- Modify: `src/MSOSync.Metadata/Operations/OperationType.cs` (find via `grep "enum OperationType" src/`)
- Create: `src/MSOSync.Persistence/Migrations/M033_RollingOperations.cs` (+ Designer via `dotnet ef`)
- Test: `tests/MSOSync.IntegrationTests/Lifecycle/` schema-count test update (Task 9 covers smoke; here just build)

**Interfaces:**
- Consumes: existing `ITenantScoped`, `TenantScoped` attribute, `AppDbContext`.
- Produces (later tasks rely on these EXACT names):
  - `NodeLifecycleState.Draining`
  - `SyncNode.AgentVersion` (`string?`), `SyncNode.DrainCompletedAt` (`DateTimeOffset?`)
  - `SyncOperationStep { Guid StepId; Guid OperationId; string NodeId; int WaveNumber; string Status; DateTime? StartedAt; DateTime? CompletedAt; string? ErrorMessage; Guid TenantId }`
  - `AppDbContext.OperationSteps` (`DbSet<SyncOperationStep>`)
  - `OperationType.RollingMaintenance`, `OperationType.RollingUpgrade`

- [ ] **Step 1: Add `Draining` to `NodeLifecycleState`**

In `src/MSOSync.Persistence/Entities/NodeLifecycleState.cs` insert after `Disabled,`:

```csharp
    Draining,             // reversible quiesce: routing excluded, in-flight completes, heartbeats accepted
```

(Stored as string — enum position is not persisted; safe to insert mid-enum.)

- [ ] **Step 2: Add columns to `SyncNode`**

In `src/MSOSync.Persistence/Entities/SyncNode.cs`, after the `MaintenanceStartedBy` property (end of Maintenance block), add:

```csharp
    // Drain (2B.1)
    public DateTimeOffset? DrainCompletedAt { get; set; }   // set once by RollingOperationWorker when outgoing queue empties; cleared on StartDrain/Resume
    public string? AgentVersion { get; set; }               // last NodeVersion reported via heartbeat
```

- [ ] **Step 3: Create `SyncOperationStep` entity**

`src/MSOSync.Persistence/Entities/SyncOperationStep.cs`:

```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncOperationStep : ITenantScoped
{
    public Guid   StepId       { get; set; }
    public Guid   OperationId  { get; set; }               // FK -> sync_operation
    public string NodeId       { get; set; } = null!;
    public int    WaveNumber   { get; set; }               // 1-based
    public string Status       { get; set; } = null!;      // RollingStepStatus as string
    public DateTime? StartedAt   { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage  { get; set; }
    public Guid   TenantId     { get; set; }
}
```

- [ ] **Step 4: Create EF configuration**

`src/MSOSync.Persistence/Configurations/SyncOperationStepConfiguration.cs` (mirror `SyncOperationConfiguration.cs` style — open it first and copy schema constant usage):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncOperationStepConfiguration : IEntityTypeConfiguration<SyncOperationStep>
{
    public void Configure(EntityTypeBuilder<SyncOperationStep> b)
    {
        b.ToTable("sync_operation_step", "msosync");
        b.HasKey(x => x.StepId);
        b.Property(x => x.StepId).HasColumnName("step_id").ValueGeneratedNever();
        b.Property(x => x.OperationId).HasColumnName("operation_id");
        b.Property(x => x.NodeId).HasColumnName("node_id").HasMaxLength(50).IsRequired();
        b.Property(x => x.WaveNumber).HasColumnName("wave_number");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        b.Property(x => x.StartedAt).HasColumnName("started_at");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at");
        b.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        b.Property(x => x.TenantId).HasColumnName("tenant_id");

        b.HasOne<SyncOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.OperationId, x.WaveNumber }).HasDatabaseName("ix_sync_operation_step_op_wave");
        b.HasIndex(x => new { x.TenantId, x.NodeId }).HasDatabaseName("ix_sync_operation_step_tenant_node");
    }
}
```

Check `SyncOperationConfiguration.cs` for exact column-name casing conventions and match them (snake_case is the repo convention).

- [ ] **Step 5: DbSet + OperationType values**

`AppDbContext.cs` — after `Operations` DbSet:

```csharp
    public DbSet<SyncOperationStep>  OperationSteps   => Set<SyncOperationStep>();
```

In the `OperationType` enum (locate with `grep -rn "enum OperationType" src/MSOSync.Metadata`), append:

```csharp
    RollingMaintenance,
    RollingUpgrade,
```

- [ ] **Step 6: M033 migration**

Match repo migration style (see `M032_DomainTenantIdMigration.cs` — attribute-annotated `[Migration("M033_RollingOperations")]`, `[DbContext(typeof(AppDbContext))]`; check M032 header verbatim first). Up:

```csharp
migrationBuilder.AddColumn<string>(name: "agent_version", schema: "msosync", table: "sync_node",
    type: "nvarchar(100)", maxLength: 100, nullable: true);
migrationBuilder.AddColumn<DateTimeOffset>(name: "drain_completed_at", schema: "msosync", table: "sync_node",
    type: "datetimeoffset", nullable: true);

migrationBuilder.CreateTable(
    name: "sync_operation_step",
    schema: "msosync",
    columns: t => new
    {
        step_id       = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
        operation_id  = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
        node_id       = t.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
        wave_number   = t.Column<int>(type: "int", nullable: false),
        status        = t.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
        started_at    = t.Column<DateTime>(type: "datetime2", nullable: true),
        completed_at  = t.Column<DateTime>(type: "datetime2", nullable: true),
        error_message = t.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
        tenant_id     = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
    },
    constraints: t =>
    {
        t.PrimaryKey("pk_sync_operation_step", x => x.step_id);
        t.ForeignKey("fk_sync_operation_step_operation", x => x.operation_id,
            principalSchema: "msosync", principalTable: "sync_operation",
            principalColumn: "operation_id", onDelete: ReferentialAction.Cascade);
    });

migrationBuilder.CreateIndex("ix_sync_operation_step_op_wave", schema: "msosync",
    table: "sync_operation_step", columns: new[] { "operation_id", "wave_number" });
migrationBuilder.CreateIndex("ix_sync_operation_step_tenant_node", schema: "msosync",
    table: "sync_operation_step", columns: new[] { "tenant_id", "node_id" });
```

Down: drop table + both columns. Verify the `sync_operation` PK column name in `SyncOperationConfiguration.cs` before writing the FK (`operation_id` expected).

Prefer generating via `dotnet ef migrations add M033_RollingOperations --project src/MSOSync.Persistence --startup-project src/MSOSync.Api` then renaming/editing to match hand-written repo style — inspect how M032's Designer/snapshot were handled and follow identically.

- [ ] **Step 7: Build + apply migration to dev DB**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
dotnet ef database update --project src/MSOSync.Persistence --startup-project src/MSOSync.Api
```

Expected: build 0 warnings; migration applies. (If dev DB unavailable, note it — M033 smoke test in Task 9 covers Testcontainers path.)

- [ ] **Step 8: Fix schema-count assertions**

Table count changes 45 → 46. Run:

```powershell
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "FullyQualifiedName~Schema" --nologo
```

If a count assertion fails (cf. 2A-016 precedent), update expected count to 46.

- [ ] **Step 9: Commit**

```powershell
git add src/MSOSync.Persistence/Entities/NodeLifecycleState.cs src/MSOSync.Persistence/Entities/SyncNode.cs src/MSOSync.Persistence/Entities/SyncOperationStep.cs src/MSOSync.Persistence/Configurations/SyncOperationStepConfiguration.cs src/MSOSync.Persistence/AppDbContext.cs src/MSOSync.Persistence/Migrations/ src/MSOSync.Metadata/Operations/
git commit -m "feat(2B.1-T1): Draining state, AgentVersion, sync_operation_step + M033"
```
