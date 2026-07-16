# Task 2: Tenant + TenantMembership Entities + M030 Migration

**Part of:** [Epic 15A Multi-Tenancy](2026-07-16-epic15a-multi-tenancy.md)

**Goal:** Create the `Tenant` and `TenantMembership` entity classes, EF configurations, add DbSets to AppDbContext, write migration M030 that creates the tables and seeds `SystemTenant`, and verify the DB schema.

**Files:**
- Create: `src/MSOSync.Persistence/Entities/Tenant.cs`
- Create: `src/MSOSync.Persistence/Entities/TenantMembership.cs`
- Create: `src/MSOSync.Persistence/Configurations/TenantConfiguration.cs`
- Create: `src/MSOSync.Persistence/Configurations/TenantMembershipConfiguration.cs`
- Create: `src/MSOSync.Persistence/Migrations/M030_MultiTenancyFoundation.cs`
- Modify: `src/MSOSync.Persistence/AppDbContext.cs` — add DbSet<Tenant>, DbSet<TenantMembership>

**Interfaces:**
- Consumes: `WellKnownTenantIds.SystemTenant` from Task 1
- Produces: `Tenant` entity, `TenantMembership` entity, `TenantStatus`, `EditionType` (already in Common), `MemberStatus` enums, `DbSet<Tenant>`, `DbSet<TenantMembership>` — consumed by Tasks 3, 4, 5, 6, 8

---

- [ ] **Step 1: Create Tenant entity**

Create `src/MSOSync.Persistence/Entities/Tenant.cs`:
```csharp
namespace MSOSync.Persistence.Entities;

public enum TenantStatus { Provisioning, Active, Suspended, Deleted }
public enum MemberStatus  { Active, Suspended }

public class Tenant
{
    public Guid            TenantId       { get; set; }
    public string          Name           { get; set; } = "";
    public string          Slug           { get; set; } = "";   // lowercase, [a-z0-9-], immutable after create
    public TenantStatus    Status         { get; set; }
    public MSOSync.Common.Tenancy.EditionType Edition { get; set; }
    public Guid?           LicenseId      { get; set; }         // wired in 15B
    public DateTimeOffset  CreatedAtUtc   { get; set; }
    public DateTimeOffset  UpdatedAtUtc   { get; set; }
    public DateTimeOffset? SuspendedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc   { get; set; }
    public byte[]          RowVersion     { get; set; } = [];
}
```

- [ ] **Step 2: Create TenantMembership entity**

Create `src/MSOSync.Persistence/Entities/TenantMembership.cs`:
```csharp
namespace MSOSync.Persistence.Entities;

public class TenantMembership
{
    public Guid           TenantId       { get; set; }
    public long           UserId         { get; set; }
    public long           RoleId         { get; set; }
    public MemberStatus   Status         { get; set; }
    public DateTimeOffset JoinedAt       { get; set; }
    public DateTimeOffset LastAccessedAt { get; set; }
    public byte[]         RowVersion     { get; set; } = [];

    public Tenant?         Tenant { get; set; }
    public SyncUser?       User   { get; set; }
    public SyncRole?       Role   { get; set; }
}
```

- [ ] **Step 3: Create EF configuration for Tenant**

Create `src/MSOSync.Persistence/Configurations/TenantConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenant", Schema);
        builder.HasKey(e => e.TenantId);

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Slug)
            .HasColumnName("slug")
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(e => e.Slug)
            .IsUnique()
            .HasDatabaseName("UQ_tenant_slug");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(e => e.Edition)
            .HasColumnName("edition")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(e => e.LicenseId)
            .HasColumnName("license_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired(false);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.SuspendedAtUtc)
            .HasColumnName("suspended_at_utc")
            .HasColumnType("datetimeoffset")
            .IsRequired(false);

        builder.Property(e => e.DeletedAtUtc)
            .HasColumnName("deleted_at_utc")
            .HasColumnType("datetimeoffset")
            .IsRequired(false);

        builder.Property(e => e.RowVersion)
            .HasColumnName("row_version")
            .IsRowVersion()
            .IsRequired();
    }
}
```

- [ ] **Step 4: Create EF configuration for TenantMembership**

Create `src/MSOSync.Persistence/Configurations/TenantMembershipConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_membership", Schema);
        builder.HasKey(e => new { e.TenantId, e.UserId });

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(e => e.RoleId)
            .HasColumnName("role_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(e => e.JoinedAt)
            .HasColumnName("joined_at")
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.LastAccessedAt)
            .HasColumnName("last_accessed_at")
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.RowVersion)
            .HasColumnName("row_version")
            .IsRowVersion()
            .IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_tenant_membership_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasConstraintName("FK_tenant_membership_user_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .HasConstraintName("FK_tenant_membership_role_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_tenant_membership_tenant_id");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_tenant_membership_user_id");
    }
}
```

- [ ] **Step 5: Add DbSets to AppDbContext**

Open `src/MSOSync.Persistence/AppDbContext.cs` and add two DbSet properties alongside the existing ones:
```csharp
public DbSet<Tenant>           Tenants           { get; set; } = null!;
public DbSet<TenantMembership> TenantMemberships { get; set; } = null!;
```

- [ ] **Step 6: Verify the build**

Run:
```
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Write migration M030**

Create `src/MSOSync.Persistence/Migrations/M030_MultiTenancyFoundation.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Common.Tenancy;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M030_MultiTenancyFoundation : Migration
{
    private const string Schema = "msosync";
    private static readonly string SystemTenantId = WellKnownTenantIds.SystemTenant.ToString();

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Create tenant table
        migrationBuilder.CreateTable(
            name: "tenant",
            schema: Schema,
            columns: table => new
            {
                tenant_id       = table.Column<Guid>            (type: "uniqueidentifier",  nullable: false),
                name            = table.Column<string>          (type: "nvarchar(200)",      maxLength: 200, nullable: false),
                slug            = table.Column<string>          (type: "nvarchar(100)",      maxLength: 100, nullable: false),
                status          = table.Column<int>             (type: "int",                nullable: false),
                edition         = table.Column<int>             (type: "int",                nullable: false),
                license_id      = table.Column<Guid>            (type: "uniqueidentifier",  nullable: true),
                created_at_utc  = table.Column<DateTimeOffset>  (type: "datetimeoffset",    nullable: false),
                updated_at_utc  = table.Column<DateTimeOffset>  (type: "datetimeoffset",    nullable: false),
                suspended_at_utc = table.Column<DateTimeOffset> (type: "datetimeoffset",   nullable: true),
                deleted_at_utc  = table.Column<DateTimeOffset>  (type: "datetimeoffset",    nullable: true),
                row_version     = table.Column<byte[]>          (type: "rowversion",         nullable: false, rowVersion: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenant", x => x.tenant_id);
            });

        migrationBuilder.CreateIndex(
            name:   "UQ_tenant_slug",
            schema: Schema,
            table:  "tenant",
            column: "slug",
            unique: true);

        // 2. Create tenant_membership table
        migrationBuilder.CreateTable(
            name: "tenant_membership",
            schema: Schema,
            columns: table => new
            {
                tenant_id       = table.Column<Guid>            (type: "uniqueidentifier",  nullable: false),
                user_id         = table.Column<long>            (type: "bigint",             nullable: false),
                role_id         = table.Column<long>            (type: "bigint",             nullable: false),
                status          = table.Column<int>             (type: "int",                nullable: false),
                joined_at       = table.Column<DateTimeOffset>  (type: "datetimeoffset",    nullable: false),
                last_accessed_at = table.Column<DateTimeOffset> (type: "datetimeoffset",   nullable: false),
                row_version     = table.Column<byte[]>          (type: "rowversion",         nullable: false, rowVersion: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenant_membership", x => new { x.tenant_id, x.user_id });
                table.ForeignKey("FK_tenant_membership_tenant_id", x => x.tenant_id,
                    principalSchema: Schema, principalTable: "tenant",    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_tenant_membership_user_id",   x => x.user_id,
                    principalSchema: Schema, principalTable: "sync_user", principalColumn: "UserId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_tenant_membership_role_id",   x => x.role_id,
                    principalSchema: Schema, principalTable: "sync_role", principalColumn: "RoleId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tenant_membership_tenant_id", schema: Schema,
            table: "tenant_membership", column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tenant_membership_user_id", schema: Schema,
            table: "tenant_membership", column: "user_id");

        // 3. Seed SystemTenant (idempotent — only if Tenants table is empty)
        var now = DateTimeOffset.UtcNow.ToString("o");
        migrationBuilder.Sql($"""
            IF NOT EXISTS (SELECT 1 FROM [{Schema}].[tenant] WHERE [tenant_id] = '{SystemTenantId}')
            BEGIN
                INSERT INTO [{Schema}].[tenant]
                    ([tenant_id], [name], [slug], [status], [edition],
                     [created_at_utc], [updated_at_utc])
                VALUES
                    ('{SystemTenantId}', 'System Tenant', 'system', 1, 0,
                     SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            END
            """);

        // 4. Seed TenantMembership for all existing ADMIN users → SystemTenant
        //    status=0 (Active), joined_at = now
        //    role_id = (SELECT RoleId FROM sync_role WHERE RoleName = 'ADMIN')
        migrationBuilder.Sql($"""
            INSERT INTO [{Schema}].[tenant_membership] ([tenant_id], [user_id], [role_id], [status], [joined_at], [last_accessed_at])
            SELECT '{SystemTenantId}', u.[UserId], r.[RoleId], 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
            FROM [{Schema}].[sync_user] u
            INNER JOIN [{Schema}].[sync_user_role] ur ON ur.[UserId] = u.[UserId]
            INNER JOIN [{Schema}].[sync_role]      r  ON r.[RoleId]  = ur.[RoleId]
            WHERE NOT EXISTS (
                SELECT 1 FROM [{Schema}].[tenant_membership] tm
                WHERE tm.[tenant_id] = '{SystemTenantId}' AND tm.[user_id] = u.[UserId]
            )
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "tenant_membership", schema: Schema);
        migrationBuilder.DropTable(name: "tenant",            schema: Schema);
    }
}
```

> **Note on FK column names:** The principal table column names (`UserId`, `RoleId`) must match the actual column names in `sync_user` and `sync_role` tables. Check the existing migrations or configurations if unsure — adjust the `principalColumn` values accordingly.

- [ ] **Step 8: Update the EF model snapshot**

EF Core requires `AppDbContextModelSnapshot.cs` to be updated when migrations are added. Run the EF migration scaffold command to let EF auto-update the snapshot (or update it manually based on the entities added):

```
dotnet ef migrations add M030_MultiTenancyFoundation --project src/MSOSync.Persistence --startup-project src/MSOSync.App -- --environment Development
```

This generates a scaffold. **Replace the generated `Up`/`Down` content** with the implementation from Step 7 above, keeping EF's auto-generated snapshot update. Alternatively, if the project uses fully hand-written migrations without EF snapshot generation, skip the scaffold and create the file manually from Step 7.

- [ ] **Step 9: Apply migration to dev database**

```
dotnet ef database update M030_MultiTenancyFoundation --project src/MSOSync.Persistence --startup-project src/MSOSync.App
```

Expected: `Done. Applied 1 migration.`

- [ ] **Step 10: Verify the schema**

Connect to the dev database and verify:
```sql
SELECT table_name FROM information_schema.tables
WHERE table_schema = 'msosync' AND table_name IN ('tenant', 'tenant_membership');
-- Expected: 2 rows

SELECT * FROM [msosync].[tenant];
-- Expected: 1 row with tenant_id = '00000000-0000-0000-0000-000000000001', slug = 'system'
```

- [ ] **Step 11: Commit**

```
git add src/MSOSync.Persistence/Entities/Tenant.cs src/MSOSync.Persistence/Entities/TenantMembership.cs
git add src/MSOSync.Persistence/Configurations/TenantConfiguration.cs src/MSOSync.Persistence/Configurations/TenantMembershipConfiguration.cs
git add src/MSOSync.Persistence/Migrations/M030_MultiTenancyFoundation.cs src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs
git add src/MSOSync.Persistence/AppDbContext.cs
git commit -m "feat(15A-2): Tenant + TenantMembership entities, M030 migration, SystemTenant seed"
```
