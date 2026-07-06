using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M020_AddRegistrationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "metadata_json",
                schema: "msosync",
                table: "sync_registration_request",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "node_name",
                schema: "msosync",
                table: "sync_registration_request",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "processed_at",
                schema: "msosync",
                table: "sync_registration_request",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processed_by",
                schema: "msosync",
                table: "sync_registration_request",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_status",
                schema: "msosync",
                table: "sync_registration_request",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "registration_type",
                schema: "msosync",
                table: "sync_registration_request",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                schema: "msosync",
                table: "sync_registration_request",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_reg_request_nodeid_status",
                schema: "msosync",
                table: "sync_registration_request",
                columns: new[] { "node_id", "registration_status" });

            migrationBuilder.CreateIndex(
                name: "IX_reg_request_status",
                schema: "msosync",
                table: "sync_registration_request",
                column: "registration_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reg_request_nodeid_status",
                schema: "msosync",
                table: "sync_registration_request");

            migrationBuilder.DropIndex(
                name: "IX_reg_request_status",
                schema: "msosync",
                table: "sync_registration_request");

            migrationBuilder.DropColumn(
                name: "metadata_json",
                schema: "msosync",
                table: "sync_registration_request");

            migrationBuilder.DropColumn(
                name: "node_name",
                schema: "msosync",
                table: "sync_registration_request");

            migrationBuilder.DropColumn(
                name: "processed_at",
                schema: "msosync",
                table: "sync_registration_request");

            migrationBuilder.DropColumn(
                name: "processed_by",
                schema: "msosync",
                table: "sync_registration_request");

            migrationBuilder.DropColumn(
                name: "registration_status",
                schema: "msosync",
                table: "sync_registration_request");

            migrationBuilder.DropColumn(
                name: "registration_type",
                schema: "msosync",
                table: "sync_registration_request");

            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "msosync",
                table: "sync_registration_request");
        }
    }
}
