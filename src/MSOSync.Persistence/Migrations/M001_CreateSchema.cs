// src/MSOSync.Persistence/Migrations/M001_CreateSchema.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000001_CreateSchema")]
public partial class M001_CreateSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "msosync");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Schema drop only safe after all tables removed by later Down() calls
    }
}
