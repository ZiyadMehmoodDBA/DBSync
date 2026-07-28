using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M039_WidenLockColumns : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Widen lock_name and lock_owner from varchar(50) to varchar(200) so that
        // per-node scheduler lock keys ("scheduler:SyncJob:<nodeId>") and FQDN owners
        // do not truncate. Fixes C1/I5 from the Phase 2C-2D code review.
        migrationBuilder.AlterColumn<string>(
            name:      "lock_name",
            schema:    Schema,
            table:     "sync_lock",
            type:      "varchar(200)",
            maxLength: 200,
            unicode:   false,
            nullable:  false,
            oldClrType:   typeof(string),
            oldType:      "varchar(50)",
            oldMaxLength: 50,
            oldUnicode:   false);

        migrationBuilder.AlterColumn<string>(
            name:      "lock_owner",
            schema:    Schema,
            table:     "sync_lock",
            type:      "varchar(200)",
            maxLength: 200,
            unicode:   false,
            nullable:  true,
            oldClrType:   typeof(string),
            oldType:      "varchar(50)",
            oldMaxLength: 50,
            oldUnicode:   false,
            oldNullable:  true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name:      "lock_name",
            schema:    Schema,
            table:     "sync_lock",
            type:      "varchar(50)",
            maxLength: 50,
            unicode:   false,
            nullable:  false,
            oldClrType:   typeof(string),
            oldType:      "varchar(200)",
            oldMaxLength: 200,
            oldUnicode:   false);

        migrationBuilder.AlterColumn<string>(
            name:      "lock_owner",
            schema:    Schema,
            table:     "sync_lock",
            type:      "varchar(50)",
            maxLength: 50,
            unicode:   false,
            nullable:  true,
            oldClrType:   typeof(string),
            oldType:      "varchar(200)",
            oldMaxLength: 200,
            oldUnicode:   false,
            oldNullable:  true);
    }
}
