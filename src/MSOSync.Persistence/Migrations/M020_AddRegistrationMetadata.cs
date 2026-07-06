using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    public partial class M020_AddRegistrationMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "node_name",
                table: "sync_registration_request",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "metadata_json",
                table: "sync_registration_request",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registration_type",
                table: "sync_registration_request",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "New");

            migrationBuilder.AddColumn<string>(
                name: "registration_status",
                table: "sync_registration_request",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "processed_at",
                table: "sync_registration_request",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processed_by",
                table: "sync_registration_request",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "sync_registration_request",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_reg_request_status",
                table: "sync_registration_request",
                column: "registration_status");

            migrationBuilder.CreateIndex(
                name: "IX_reg_request_nodeid_status",
                table: "sync_registration_request",
                columns: new[] { "node_id", "registration_status" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("IX_reg_request_nodeid_status", "sync_registration_request");
            migrationBuilder.DropIndex("IX_reg_request_status", "sync_registration_request");
            migrationBuilder.DropColumn("row_version", "sync_registration_request");
            migrationBuilder.DropColumn("processed_by", "sync_registration_request");
            migrationBuilder.DropColumn("processed_at", "sync_registration_request");
            migrationBuilder.DropColumn("registration_status", "sync_registration_request");
            migrationBuilder.DropColumn("registration_type", "sync_registration_request");
            migrationBuilder.DropColumn("metadata_json", "sync_registration_request");
            migrationBuilder.DropColumn("node_name", "sync_registration_request");
        }
    }
}
