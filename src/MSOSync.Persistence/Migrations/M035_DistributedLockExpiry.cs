using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M035_DistributedLockExpiry : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name:     "lock_expiry",
            schema:   Schema,
            table:    "sync_lock",
            type:     "datetime2(7)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name:   "lock_expiry",
            schema: Schema,
            table:  "sync_lock");
    }
}
