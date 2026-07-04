using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M019_ExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_export_job",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    parent_job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    resource_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    filters_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    progress_percent = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    row_count = table.Column<long>(type: "bigint", nullable: true),
                    output_path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_export_job", x => x.job_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_export_job_requested_by",
                table: "sync_export_job",
                columns: new[] { "requested_by", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_export_job_status_created",
                table: "sync_export_job",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_export_job");
        }
    }
}
