using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    [Migration("20260619000010_NodeSecurityHashes")]
    /// <inheritdoc />
    public partial class M010_NodeSecurityHashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "current_token_hash",
                schema: "msosync",
                table: "sync_node_security",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "next_token_hash",
                schema: "msosync",
                table: "sync_node_security",
                type: "varchar(255)",
                unicode: false,
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "next_token_hash",
                schema: "msosync",
                table: "sync_node_security");

            migrationBuilder.DropColumn(
                name: "current_token_hash",
                schema: "msosync",
                table: "sync_node_security");
        }
    }
}
