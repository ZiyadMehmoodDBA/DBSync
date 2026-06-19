// src/MSOSync.Persistence/Migrations/M011_RemovePlaintextNodeToken.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000011_RemovePlaintextNodeToken")]
public partial class M011_RemovePlaintextNodeToken : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // IF EXISTS guard — resilient against partially migrated DBs
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('msosync.sync_node_security')
                  AND name = 'node_token'
            )
            BEGIN
                ALTER TABLE msosync.sync_node_security DROP COLUMN node_token
            END
            """);

        migrationBuilder.AddColumn<DateTime>(
            name: "rotation_scheduled",
            schema: "msosync",
            table: "sync_node_security",
            type: "datetime2",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "rotation_scheduled",
            schema: "msosync",
            table: "sync_node_security");

        migrationBuilder.AddColumn<string>(
            name: "node_token",
            schema: "msosync",
            table: "sync_node_security",
            type: "varchar(255)",
            unicode: false,
            maxLength: 255,
            nullable: true);
    }
}
