# Epic 12C — Task 2: M025_ParameterMetadata Migration

**Branch:** `feat/epic12c-system-admin` (same branch as Task 1)  
**Files touched:** 4 (1 create migration, 1 modify entity, 1 modify configuration, 0 new AppDbContext changes needed)  
**Depends on:** Task 1 complete (M024 in place).

---

## Context

`SyncParameter` currently has only two columns: `parameter_name` and `parameter_value`. This task adds ten metadata columns that turn raw key-value pairs into a fully-described, UI-friendly configuration system. It also seeds two categories of parameters:

- **Feature flags** (`category = 'FeatureFlag'`, `value_type = 'Boolean'`) — boolean toggles for runtime features.
- **Retention policies** (`category = 'Retention'`, `value_type = 'Integer'`) — data-retention thresholds in days/hours.

The seed data uses `migrationBuilder.InsertData` so the rows are idempotent on a fresh schema. In production environments that already have some of these keys, the `Down()` seed removal uses `DeleteData` matching on `parameter_name`.

---

## Steps

- [ ] **1. Add ten new properties to SyncParameter**

  Open `src/MSOSync.Persistence/Entities/SyncParameter.cs`. The current content is:

  ```csharp
  namespace MSOSync.Persistence.Entities;

  public sealed class SyncParameter
  {
      public string ParameterName  { get; set; } = null!;
      public string? ParameterValue { get; set; }
  }
  ```

  Replace the entire file with:

  ```csharp
  namespace MSOSync.Persistence.Entities;

  public sealed class SyncParameter
  {
      public string  ParameterName   { get; set; } = null!;
      public string? ParameterValue  { get; set; }

      // ── M025: parameter metadata ───────────────────────────────────────────────
      public string? Category       { get; set; }   // e.g. FeatureFlag, Retention
      public string? DisplayName    { get; set; }
      public string? Description    { get; set; }
      public int?    DisplayOrder   { get; set; }
      public string? ValueType      { get; set; }   // Boolean|Integer|String|TimeSpan|Duration|Enum
      public string? MinimumValue   { get; set; }
      public string? MaximumValue   { get; set; }
      public string? AllowedValues  { get; set; }   // JSON array of allowed string values
      public string? DependsOn      { get; set; }   // other parameter_name this one depends on
      public string? ConflictsWith  { get; set; }   // other parameter_name this one conflicts with
  }
  ```

- [ ] **2. Update SyncParameterConfiguration to map the new columns**

  Open `src/MSOSync.Persistence/Configurations/SyncParameterConfiguration.cs`. The current content is:

  ```csharp
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

  Replace the entire file with:

  ```csharp
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

          builder.Property(e => e.ParameterName)
              .HasColumnName("parameter_name")
              .HasColumnType("varchar(100)")
              .HasMaxLength(100)
              .IsUnicode(false);

          builder.Property(e => e.ParameterValue)
              .HasColumnName("parameter_value")
              .HasColumnType("nvarchar(max)");

          // ── M025: metadata columns ─────────────────────────────────────────────

          builder.Property(e => e.Category)
              .HasColumnName("category")
              .HasColumnType("varchar(50)")
              .HasMaxLength(50)
              .IsUnicode(false);

          builder.Property(e => e.DisplayName)
              .HasColumnName("display_name")
              .HasColumnType("nvarchar(200)")
              .HasMaxLength(200);

          builder.Property(e => e.Description)
              .HasColumnName("description")
              .HasColumnType("nvarchar(1000)")
              .HasMaxLength(1000);

          builder.Property(e => e.DisplayOrder)
              .HasColumnName("display_order");

          builder.Property(e => e.ValueType)
              .HasColumnName("value_type")
              .HasColumnType("varchar(30)")
              .HasMaxLength(30)
              .IsUnicode(false);

          builder.Property(e => e.MinimumValue)
              .HasColumnName("minimum_value")
              .HasColumnType("varchar(100)")
              .HasMaxLength(100)
              .IsUnicode(false);

          builder.Property(e => e.MaximumValue)
              .HasColumnName("maximum_value")
              .HasColumnType("varchar(100)")
              .HasMaxLength(100)
              .IsUnicode(false);

          builder.Property(e => e.AllowedValues)
              .HasColumnName("allowed_values")
              .HasColumnType("nvarchar(max)");

          builder.Property(e => e.DependsOn)
              .HasColumnName("depends_on")
              .HasColumnType("varchar(200)")
              .HasMaxLength(200)
              .IsUnicode(false);

          builder.Property(e => e.ConflictsWith)
              .HasColumnName("conflicts_with")
              .HasColumnType("varchar(200)")
              .HasMaxLength(200)
              .IsUnicode(false);
      }
  }
  ```

- [ ] **3. Create the migration file**

  Create `src/MSOSync.Persistence/Migrations/M025_ParameterMetadata.cs`:

  ```csharp
  using Microsoft.EntityFrameworkCore.Migrations;

  #nullable disable

  namespace MSOSync.Persistence.Migrations
  {
      /// <inheritdoc />
      public partial class M025_ParameterMetadata : Migration
      {
          /// <inheritdoc />
          protected override void Up(MigrationBuilder migrationBuilder)
          {
              // ── Add metadata columns to sync_parameter ─────────────────────────

              migrationBuilder.AddColumn<string>(
                  name: "category",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "varchar(50)",
                  unicode: false,
                  maxLength: 50,
                  nullable: true);

              migrationBuilder.AddColumn<string>(
                  name: "display_name",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "nvarchar(200)",
                  maxLength: 200,
                  nullable: true);

              migrationBuilder.AddColumn<string>(
                  name: "description",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "nvarchar(1000)",
                  maxLength: 1000,
                  nullable: true);

              migrationBuilder.AddColumn<int>(
                  name: "display_order",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "int",
                  nullable: true);

              migrationBuilder.AddColumn<string>(
                  name: "value_type",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "varchar(30)",
                  unicode: false,
                  maxLength: 30,
                  nullable: true);

              migrationBuilder.AddColumn<string>(
                  name: "minimum_value",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "varchar(100)",
                  unicode: false,
                  maxLength: 100,
                  nullable: true);

              migrationBuilder.AddColumn<string>(
                  name: "maximum_value",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "varchar(100)",
                  unicode: false,
                  maxLength: 100,
                  nullable: true);

              migrationBuilder.AddColumn<string>(
                  name: "allowed_values",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "nvarchar(max)",
                  nullable: true);

              migrationBuilder.AddColumn<string>(
                  name: "depends_on",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "varchar(200)",
                  unicode: false,
                  maxLength: 200,
                  nullable: true);

              migrationBuilder.AddColumn<string>(
                  name: "conflicts_with",
                  schema: "msosync",
                  table: "sync_parameter",
                  type: "varchar(200)",
                  unicode: false,
                  maxLength: 200,
                  nullable: true);

              // ── Seed: Feature Flags ────────────────────────────────────────────
              // InsertData is idempotent on fresh databases. On an existing DB that
              // already has one of these rows (from M008 seed), the migration will
              // raise a PK violation. In that case, replace InsertData with UpdateData
              // for the value + metadata columns only.

              migrationBuilder.InsertData(
                  schema: "msosync",
                  table: "sync_parameter",
                  columns: new[]
                  {
                      "parameter_name", "parameter_value",
                      "category", "display_name", "description",
                      "display_order", "value_type"
                  },
                  values: new object[,]
                  {
                      {
                          "EnableConfigurationRollout", "true",
                          "FeatureFlag", "Enable Configuration Rollout",
                          "Enables the configuration rollout engine. When false, rollout requests are accepted but not dispatched.",
                          10, "Boolean"
                      },
                      {
                          "EnableTopologyEditing", "false",
                          "FeatureFlag", "Enable Topology Editing",
                          "Allows operators to modify topology edges (channels, routers) from the UI.",
                          20, "Boolean"
                      },
                      {
                          "EnableExperimentalUI", "false",
                          "FeatureFlag", "Enable Experimental UI",
                          "Shows experimental dashboard panels and UI features not yet promoted to stable.",
                          30, "Boolean"
                      },
                      {
                          "EnableBackgroundCleanup", "true",
                          "FeatureFlag", "Enable Background Cleanup",
                          "Enables the background worker that purges expired export jobs and old operation records.",
                          40, "Boolean"
                      },
                      {
                          "EnableExportJobs", "true",
                          "FeatureFlag", "Enable Export Jobs",
                          "Enables the export job subsystem. When false, POST /export-jobs returns 503.",
                          50, "Boolean"
                      },
                  });

              // ── Seed: Retention Policies ───────────────────────────────────────

              migrationBuilder.InsertData(
                  schema: "msosync",
                  table: "sync_parameter",
                  columns: new[]
                  {
                      "parameter_name", "parameter_value",
                      "category", "display_name", "description",
                      "display_order", "value_type",
                      "minimum_value", "maximum_value"
                  },
                  values: new object[,]
                  {
                      {
                          "Retention.AuditDays", "90",
                          "Retention", "Audit Log Retention (days)",
                          "Number of days to retain audit log entries. Entries older than this are purged by the background cleanup worker.",
                          110, "Integer",
                          "1", "3650"
                      },
                      {
                          "Retention.OperationDays", "180",
                          "Retention", "Operation Record Retention (days)",
                          "Number of days to retain completed/failed operation records in sync_operation.",
                          120, "Integer",
                          "1", "3650"
                      },
                      {
                          "Retention.ConnectivityHistoryDays", "30",
                          "Retention", "Connectivity History Retention (days)",
                          "Number of days to retain node connectivity history rows.",
                          130, "Integer",
                          "1", "365"
                      },
                      {
                          "Retention.LifecycleHistoryDays", "365",
                          "Retention", "Lifecycle History Retention (days)",
                          "Number of days to retain node lifecycle transition history rows.",
                          140, "Integer",
                          "1", "3650"
                      },
                      {
                          "Retention.ExportJobHours", "24",
                          "Retention", "Export Job Retention (hours)",
                          "Number of hours a completed or failed export job file is retained before expiry.",
                          150, "Integer",
                          "1", "720"
                      },
                  });
          }

          /// <inheritdoc />
          protected override void Down(MigrationBuilder migrationBuilder)
          {
              // Remove seed data first
              migrationBuilder.DeleteData(
                  schema: "msosync",
                  table: "sync_parameter",
                  keyColumn: "parameter_name",
                  keyValues: new object[]
                  {
                      "EnableConfigurationRollout",
                      "EnableTopologyEditing",
                      "EnableExperimentalUI",
                      "EnableBackgroundCleanup",
                      "EnableExportJobs",
                      "Retention.AuditDays",
                      "Retention.OperationDays",
                      "Retention.ConnectivityHistoryDays",
                      "Retention.LifecycleHistoryDays",
                      "Retention.ExportJobHours",
                  });

              // Drop metadata columns
              migrationBuilder.DropColumn(name: "conflicts_with",  schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "depends_on",      schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "allowed_values",  schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "maximum_value",   schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "minimum_value",   schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "value_type",      schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "display_order",   schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "description",     schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "display_name",    schema: "msosync", table: "sync_parameter");
              migrationBuilder.DropColumn(name: "category",        schema: "msosync", table: "sync_parameter");
          }
      }
  }
  ```

  > **PK-conflict guard:** If the target database was seeded by M008 and already contains any of the `EnableConfigurationRollout` / `Retention.*` rows (from a prior manual seed), the `InsertData` calls will fail with a primary-key violation at migration time. In that case, split the Up() into two phases:
  > 1. `AddColumn` calls (no change needed).
  > 2. For each pre-existing key: use `migrationBuilder.UpdateData` to set the new metadata columns. For each new key not yet present: use `migrationBuilder.InsertData`. The simplest approach is to use raw SQL via `migrationBuilder.Sql("IF NOT EXISTS (...) INSERT ... ELSE UPDATE ...")`.

- [ ] **4. Build**

  ```powershell
  dotnet build src\MSOSync.Persistence\MSOSync.Persistence.csproj
  ```

  Expected: 0 errors. The ten new properties on `SyncParameter` must resolve cleanly.

- [ ] **5. Run metadata unit tests**

  ```powershell
  dotnet test tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --no-build
  ```

  `ParameterMetadataServiceTests` relies on `SyncParameter` having only `ParameterName` and `ParameterValue`. The ten new columns are all nullable, so existing test setup lines like:

  ```csharp
  db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
  ```

  …continue to compile and work without any changes to the test file.

- [ ] **6. Smoke-test the seed data in integration tests**

  The integration test suite runs against SQLite (in-memory), which uses `EnsureCreated()` — it does not run migrations. Seed data inserted via `InsertData` in the migration is **not** automatically seeded in SQLite tests. This is expected. If you need the seed data in integration tests, add a helper in `DatabaseFixture.cs` or `TestDbContext.Create()`:

  ```csharp
  // After db.Database.EnsureCreated():
  db.Parameters.AddRange(
      new SyncParameter { ParameterName = "EnableConfigurationRollout", ParameterValue = "true", Category = "FeatureFlag", ValueType = "Boolean" },
      // ... remaining seeds ...
  );
  await db.SaveChangesAsync();
  ```

  This is optional for Task 2 but required if Task 3 or 5 tests read these parameters.

- [ ] **7. Verify ParameterDescriptor in MSOSync.Metadata still compiles**

  Open `src/MSOSync.Metadata/Descriptors/ParameterDescriptor.cs` and confirm it does not hard-code the old two-column shape of `SyncParameter`. If it maps `SyncParameter` to a DTO, add the ten new nullable fields to the DTO and mapping so the compiler catches any gaps:

  ```powershell
  dotnet build src\MSOSync.Metadata\MSOSync.Metadata.csproj
  ```

- [ ] **8. Commit**

  ```powershell
  git add src\MSOSync.Persistence\Entities\SyncParameter.cs `
          src\MSOSync.Persistence\Configurations\SyncParameterConfiguration.cs `
          src\MSOSync.Persistence\Migrations\M025_ParameterMetadata.cs
  git commit -m "feat(12C-2): M025_ParameterMetadata — add 10 metadata cols + seed feature-flags and retention params"
  ```

---

## Acceptance criteria

- `dotnet build src\MSOSync.Persistence` passes with 0 errors.
- `dotnet build src\MSOSync.Metadata` passes with 0 errors.
- `SyncParameter` has exactly 12 properties (2 original + 10 new).
- `SyncParameterConfiguration.Configure` maps all 12 properties to their correct column names and types.
- Migration `Up()` adds exactly 10 `AddColumn` calls and inserts exactly 10 seed rows (5 feature flags + 5 retention policies).
- Migration `Down()` deletes the 10 seed rows and drops all 10 columns in reverse order.
- `dotnet test tests\MSOSync.MetadataTests` passes without any test modifications.
