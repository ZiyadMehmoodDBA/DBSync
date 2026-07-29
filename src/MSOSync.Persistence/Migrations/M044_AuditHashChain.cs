using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("M044_AuditHashChain")]
public partial class M044_AuditHashChain : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name:      "prev_hash",
            schema:    Schema,
            table:     "sync_audit",
            type:      "nvarchar(64)",
            maxLength: 64,
            nullable:  true);

        migrationBuilder.AddColumn<string>(
            name:      "entry_hash",
            schema:    Schema,
            table:     "sync_audit",
            type:      "nvarchar(64)",
            maxLength: 64,
            nullable:  true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "prev_hash", schema: Schema, table: "sync_audit");
        migrationBuilder.DropColumn(name: "entry_hash", schema: Schema, table: "sync_audit");
    }
}
