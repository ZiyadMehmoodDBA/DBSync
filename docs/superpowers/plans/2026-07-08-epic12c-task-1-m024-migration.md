# Epic 12C — Task 1: M024_OperationsFoundation Migration

**Branch:** `feat/epic12c-system-admin`  
**Files touched:** 4 (2 create, 1 create config, 1 modify)  
**Depends on:** M023 must already be in the Migrations folder (current latest is M016 — if the project has skipped numbers, confirm the highest existing migration before naming yours M024).

---

## Context

This task creates the `sync_operation` table that backs every long-running job in Epic 12C — exports, rollouts, and decommissions. It follows the exact migration + entity + EF Core configuration pattern used throughout the codebase.

The `SyncNodeLifecycleHistory` entity already has a `CorrelationId` column (`Guid?`). The `SyncNodeConfigurationHistory` entity already has a `CorrelationId` column (`string?`). The `SyncAudit` entity already has a `CorrelationId` column (`string?`). The plan adds composite/single-column indexes on those existing tables to support cross-table JOIN on `correlation_id` without full scans.

---

## Steps

- [ ] **1. Verify the current highest migration number**

  Open a terminal in `D:\MSOSync` and run:

  ```powershell
  Get-ChildItem src\MSOSync.Persistence\Migrations\M0*.cs |
      Where-Object { $_.Name -notmatch '\.Designer\.cs$' } |
      Sort-Object Name | Select-Object -Last 1 Name
  ```

  Confirm the file is `M016_NodeDbConnection.cs`. The next migration is `M024_OperationsFoundation` (numbers M017-M023 were planned for earlier epics; use M024 regardless of gaps so it sorts correctly).

- [ ] **2. Create the SyncOperation entity**

  Create `src/MSOSync.Persistence/Entities/SyncOperation.cs`:

  ```csharp
  namespace MSOSync.Persistence.Entities;

  public sealed class SyncOperation
  {
      public Guid   OperationId      { get; set; }
      public string OperationType    { get; set; } = null!;   // Export|Rollout|Decommission|Recovery
      public Guid?  ReferenceId      { get; set; }            // FK to the domain object (job_id / rollout_id / node_id)
      public string Status           { get; set; } = null!;   // Pending|Running|Completed|Failed|Cancelled
      public string? Result          { get; set; }            // Success|PartialSuccess|Failure|Cancelled
      public string Source           { get; set; } = null!;   // User|System|Scheduler|Worker|Api
      public int?   ProgressPercent  { get; set; }
      public string? ProgressMessage { get; set; }
      public string? CorrelationId   { get; set; }
      public Guid?  InitiatedBy      { get; set; }
      public string? MetadataJson    { get; set; }
      public string? Summary         { get; set; }
      public bool   CanCancel        { get; set; }
      public bool   CanRetry         { get; set; }
      public DateTime  StartedAt     { get; set; }
      public DateTime? CompletedAt   { get; set; }
  }
  ```

- [ ] **3. Create the EF Core configuration**

  Create `src/MSOSync.Persistence/Configurations/SyncOperationConfiguration.cs`:

  ```csharp
  using Microsoft.EntityFrameworkCore;
  using Microsoft.EntityFrameworkCore.Metadata.Builders;
  using MSOSync.Persistence.Entities;

  namespace MSOSync.Persistence.Configurations;

  public sealed class SyncOperationConfiguration : IEntityTypeConfiguration<SyncOperation>
  {
      private static readonly string Schema =
          Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

      public void Configure(EntityTypeBuilder<SyncOperation> b)
      {
          b.ToTable("sync_operation", Schema);
          b.HasKey(x => x.OperationId);

          b.Property(x => x.OperationId)
              .HasColumnName("operation_id")
              .HasDefaultValueSql("NEWID()");

          b.Property(x => x.OperationType)
              .HasColumnName("operation_type")
              .HasColumnType("varchar(50)")
              .HasMaxLength(50)
              .IsUnicode(false)
              .IsRequired();

          b.Property(x => x.ReferenceId)
              .HasColumnName("reference_id");

          b.Property(x => x.Status)
              .HasColumnName("status")
              .HasColumnType("varchar(30)")
              .HasMaxLength(30)
              .IsUnicode(false)
              .IsRequired();

          b.Property(x => x.Result)
              .HasColumnName("result")
              .HasColumnType("varchar(30)")
              .HasMaxLength(30)
              .IsUnicode(false);

          b.Property(x => x.Source)
              .HasColumnName("source")
              .HasColumnType("varchar(30)")
              .HasMaxLength(30)
              .IsUnicode(false)
              .IsRequired();

          b.Property(x => x.ProgressPercent)
              .HasColumnName("progress_percent");

          b.Property(x => x.ProgressMessage)
              .HasColumnName("progress_message")
              .HasColumnType("varchar(500)")
              .HasMaxLength(500)
              .IsUnicode(false);

          b.Property(x => x.CorrelationId)
              .HasColumnName("correlation_id")
              .HasColumnType("varchar(100)")
              .HasMaxLength(100)
              .IsUnicode(false);

          b.Property(x => x.InitiatedBy)
              .HasColumnName("initiated_by");

          b.Property(x => x.MetadataJson)
              .HasColumnName("metadata_json")
              .HasColumnType("nvarchar(2000)")
              .HasMaxLength(2000);

          b.Property(x => x.Summary)
              .HasColumnName("summary")
              .HasColumnType("varchar(500)")
              .HasMaxLength(500)
              .IsUnicode(false);

          b.Property(x => x.CanCancel)
              .HasColumnName("can_cancel")
              .HasDefaultValue(false);

          b.Property(x => x.CanRetry)
              .HasColumnName("can_retry")
              .HasDefaultValue(false);

          b.Property(x => x.StartedAt)
              .HasColumnName("started_at")
              .HasColumnType("datetime2(7)")
              .IsRequired();

          b.Property(x => x.CompletedAt)
              .HasColumnName("completed_at")
              .HasColumnType("datetime2(7)");

          // Indexes
          b.HasIndex(x => x.Status)
              .HasDatabaseName("IX_sync_operation_status");

          b.HasIndex(x => x.OperationType)
              .HasDatabaseName("IX_sync_operation_type");

          b.HasIndex(x => x.StartedAt)
              .IsDescending(true)
              .HasDatabaseName("IX_sync_operation_started_at_desc");

          b.HasIndex(x => x.CorrelationId)
              .HasDatabaseName("IX_sync_operation_correlation_id");
      }
  }
  ```

- [ ] **4. Create the migration file**

  Create `src/MSOSync.Persistence/Migrations/M024_OperationsFoundation.cs`:

  ```csharp
  using Microsoft.EntityFrameworkCore.Migrations;

  #nullable disable

  namespace MSOSync.Persistence.Migrations
  {
      /// <inheritdoc />
      public partial class M024_OperationsFoundation : Migration
      {
          /// <inheritdoc />
          protected override void Up(MigrationBuilder migrationBuilder)
          {
              migrationBuilder.CreateTable(
                  name: "sync_operation",
                  schema: "msosync",
                  columns: table => new
                  {
                      operation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false,
                          defaultValueSql: "NEWID()"),
                      operation_type = table.Column<string>(type: "varchar(50)", unicode: false,
                          maxLength: 50, nullable: false),
                      reference_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                      status = table.Column<string>(type: "varchar(30)", unicode: false,
                          maxLength: 30, nullable: false),
                      result = table.Column<string>(type: "varchar(30)", unicode: false,
                          maxLength: 30, nullable: true),
                      source = table.Column<string>(type: "varchar(30)", unicode: false,
                          maxLength: 30, nullable: false),
                      progress_percent = table.Column<int>(type: "int", nullable: true),
                      progress_message = table.Column<string>(type: "varchar(500)", unicode: false,
                          maxLength: 500, nullable: true),
                      correlation_id = table.Column<string>(type: "varchar(100)", unicode: false,
                          maxLength: 100, nullable: true),
                      initiated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                      metadata_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000,
                          nullable: true),
                      summary = table.Column<string>(type: "varchar(500)", unicode: false,
                          maxLength: 500, nullable: true),
                      can_cancel = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                      can_retry = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                      started_at = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                      completed_at = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                  },
                  constraints: table =>
                  {
                      table.PrimaryKey("PK_sync_operation", x => x.operation_id);
                  });

              // Indexes on sync_operation
              migrationBuilder.CreateIndex(
                  name: "IX_sync_operation_status",
                  schema: "msosync",
                  table: "sync_operation",
                  column: "status");

              migrationBuilder.CreateIndex(
                  name: "IX_sync_operation_type",
                  schema: "msosync",
                  table: "sync_operation",
                  column: "operation_type");

              migrationBuilder.CreateIndex(
                  name: "IX_sync_operation_started_at_desc",
                  schema: "msosync",
                  table: "sync_operation",
                  column: "started_at",
                  descending: new[] { true });

              migrationBuilder.CreateIndex(
                  name: "IX_sync_operation_correlation_id",
                  schema: "msosync",
                  table: "sync_operation",
                  column: "correlation_id");

              // Cross-table correlation index on sync_audit
              migrationBuilder.CreateIndex(
                  name: "IX_sync_audit_correlation_create_time",
                  schema: "msosync",
                  table: "sync_audit",
                  columns: new[] { "correlation_id", "create_time" });

              // Cross-table correlation index on sync_node_lifecycle_history
              // Guard: only add if the column exists (it was added in M022).
              // In SQL Server you can check INFORMATION_SCHEMA at migration time,
              // but since this column was guaranteed by M022, we add it unconditionally.
              migrationBuilder.CreateIndex(
                  name: "IX_node_lifecycle_history_correlation_id",
                  schema: "msosync",
                  table: "sync_node_lifecycle_history",
                  column: "correlation_id");

              // Cross-table correlation index on sync_node_configuration_history
              migrationBuilder.CreateIndex(
                  name: "IX_node_config_history_correlation_id",
                  schema: "msosync",
                  table: "sync_node_configuration_history",
                  column: "correlation_id");
          }

          /// <inheritdoc />
          protected override void Down(MigrationBuilder migrationBuilder)
          {
              // Remove cross-table indexes first (they don't depend on the new table)
              migrationBuilder.DropIndex(
                  name: "IX_node_config_history_correlation_id",
                  schema: "msosync",
                  table: "sync_node_configuration_history");

              migrationBuilder.DropIndex(
                  name: "IX_node_lifecycle_history_correlation_id",
                  schema: "msosync",
                  table: "sync_node_lifecycle_history");

              migrationBuilder.DropIndex(
                  name: "IX_sync_audit_correlation_create_time",
                  schema: "msosync",
                  table: "sync_audit");

              migrationBuilder.DropTable(
                  name: "sync_operation",
                  schema: "msosync");
          }
      }
  }
  ```

  > **Note on `descending` parameter:** The `MigrationBuilder.CreateIndex` overload that accepts `bool[] descending` was introduced in EF Core 7. Since this project targets .NET 9 / EF Core 9, it is available. If the build complains, replace the descending index with a raw SQL call:
  > ```csharp
  > migrationBuilder.Sql(
  >     "CREATE INDEX IX_sync_operation_started_at_desc " +
  >     "ON [msosync].[sync_operation] (started_at DESC);");
  > ```

- [ ] **5. Add the DbSet to AppDbContext**

  Open `src/MSOSync.Persistence/AppDbContext.cs` and add one line after the `ExportJobs` DbSet (line 45):

  ```csharp
  public DbSet<SyncOperation> Operations => Set<SyncOperation>();
  ```

  The file should now read (excerpt):

  ```csharp
  public DbSet<SyncExportJob>      ExportJobs       => Set<SyncExportJob>();
  public DbSet<SyncOperation>      Operations       => Set<SyncOperation>();
  ```

- [ ] **6. Build the Persistence project**

  ```powershell
  dotnet build src\MSOSync.Persistence\MSOSync.Persistence.csproj
  ```

  Expected: 0 errors. If you see `CS0246` for `Guid` or `DateTime`, add `using System;` at the top of `SyncOperation.cs` — though the project likely has global usings already.

- [ ] **7. Run existing tests to confirm nothing is broken**

  ```powershell
  dotnet test tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --no-build
  dotnet test tests\MSOSync.IntegrationTests\MSOSync.IntegrationTests.csproj --no-build
  ```

  All previously-passing tests must still pass. The new entity participates in `EnsureCreated()` used by `TestDbContext.Create()`, so SQLite in-memory tests will automatically include the new table.

- [ ] **8. Verify AppDbContext model snapshot (optional but recommended)**

  EF Core uses `*ModelSnapshot.cs` files for migration diffing. Because this project uses hand-written migrations (not `dotnet ef migrations add`), there is no snapshot to update. Confirm by checking that no `*ModelSnapshot.cs` or `AppDbContextModelSnapshot.cs` exists:

  ```powershell
  Get-ChildItem src\MSOSync.Persistence\Migrations\*Snapshot* -ErrorAction SilentlyContinue
  ```

  If a snapshot exists, open it and add the `SyncOperation` entity block to it so that future `dotnet ef migrations add` commands do not re-generate M024. Omit this step if no snapshot is present.

- [ ] **9. Commit**

  ```powershell
  git add src\MSOSync.Persistence\Entities\SyncOperation.cs `
          src\MSOSync.Persistence\Configurations\SyncOperationConfiguration.cs `
          src\MSOSync.Persistence\Migrations\M024_OperationsFoundation.cs `
          src\MSOSync.Persistence\AppDbContext.cs
  git commit -m "feat(12C-1): M024_OperationsFoundation — sync_operation table + cross-table correlation indexes"
  ```

---

## Acceptance criteria

- `dotnet build src\MSOSync.Persistence` succeeds with 0 errors.
- `db.Operations` is accessible from any service that takes `AppDbContext`.
- `TestDbContext.Create()` (SQLite in-memory) includes the `sync_operation` table — confirmed by the existing test suite passing without schema errors.
- The four indexes on `sync_operation` are present in the migration `Up()`.
- The three cross-table correlation indexes are present in the migration `Up()`.
- `Down()` drops all new objects in reverse order without touching any other data.
