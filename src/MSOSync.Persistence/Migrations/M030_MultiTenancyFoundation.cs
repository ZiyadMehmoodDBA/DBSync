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
                tenant_id        = table.Column<Guid>           (type: "uniqueidentifier", nullable: false),
                name             = table.Column<string>         (type: "nvarchar(200)",    maxLength: 200, nullable: false),
                slug             = table.Column<string>         (type: "nvarchar(100)",    maxLength: 100, nullable: false),
                status           = table.Column<int>            (type: "int",              nullable: false),
                edition          = table.Column<int>            (type: "int",              nullable: false),
                license_id       = table.Column<Guid>           (type: "uniqueidentifier", nullable: true),
                created_at_utc   = table.Column<DateTimeOffset> (type: "datetimeoffset",   nullable: false),
                updated_at_utc   = table.Column<DateTimeOffset> (type: "datetimeoffset",   nullable: false),
                suspended_at_utc = table.Column<DateTimeOffset> (type: "datetimeoffset",   nullable: true),
                deleted_at_utc   = table.Column<DateTimeOffset> (type: "datetimeoffset",   nullable: true),
                row_version      = table.Column<byte[]>         (type: "rowversion",       nullable: false, rowVersion: true),
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
                tenant_id        = table.Column<Guid>           (type: "uniqueidentifier", nullable: false),
                user_id          = table.Column<long>           (type: "bigint",           nullable: false),
                role_id          = table.Column<long>           (type: "bigint",           nullable: false),
                status           = table.Column<int>            (type: "int",              nullable: false),
                joined_at        = table.Column<DateTimeOffset> (type: "datetimeoffset",   nullable: false),
                last_accessed_at = table.Column<DateTimeOffset> (type: "datetimeoffset",   nullable: false),
                row_version      = table.Column<byte[]>         (type: "rowversion",       nullable: false, rowVersion: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenant_membership", x => new { x.tenant_id, x.user_id });
                table.ForeignKey("FK_tenant_membership_tenant_id", x => x.tenant_id,
                    principalSchema: Schema, principalTable: "tenant",    principalColumn: "tenant_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_tenant_membership_user_id",   x => x.user_id,
                    principalSchema: Schema, principalTable: "sync_user", principalColumn: "user_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_tenant_membership_role_id",   x => x.role_id,
                    principalSchema: Schema, principalTable: "sync_role", principalColumn: "role_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tenant_membership_tenant_id", schema: Schema,
            table: "tenant_membership", column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_tenant_membership_user_id", schema: Schema,
            table: "tenant_membership", column: "user_id");

        // 3. Seed SystemTenant (idempotent — only if row with that ID is absent)
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

        // 4. Seed TenantMembership for all existing users → SystemTenant
        //    status=0 (Active), joined_at = now
        migrationBuilder.Sql($"""
            INSERT INTO [{Schema}].[tenant_membership] ([tenant_id], [user_id], [role_id], [status], [joined_at], [last_accessed_at])
            SELECT '{SystemTenantId}', u.[user_id], r.[role_id], 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
            FROM [{Schema}].[sync_user] u
            INNER JOIN [{Schema}].[sync_user_role] ur ON ur.[user_id] = u.[user_id]
            INNER JOIN [{Schema}].[sync_role]      r  ON r.[role_id]  = ur.[role_id]
            WHERE NOT EXISTS (
                SELECT 1 FROM [{Schema}].[tenant_membership] tm
                WHERE tm.[tenant_id] = '{SystemTenantId}' AND tm.[user_id] = u.[user_id]
            )
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "tenant_membership", schema: Schema);
        migrationBuilder.DropTable(name: "tenant",            schema: Schema);
    }
}
