using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M025_ParameterMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Add metadata columns to sync_parameter ─────────────────────────

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "msosync",
                table: "sync_parameter",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                schema: "msosync",
                table: "sync_parameter",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "msosync",
                table: "sync_parameter",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "display_order",
                schema: "msosync",
                table: "sync_parameter",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "value_type",
                schema: "msosync",
                table: "sync_parameter",
                type: "varchar(30)",
                unicode: false,
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "minimum_value",
                schema: "msosync",
                table: "sync_parameter",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "maximum_value",
                schema: "msosync",
                table: "sync_parameter",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "allowed_values",
                schema: "msosync",
                table: "sync_parameter",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "depends_on",
                schema: "msosync",
                table: "sync_parameter",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "conflicts_with",
                schema: "msosync",
                table: "sync_parameter",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            // ── Seed: Feature Flags ────────────────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO [msosync].[sync_parameter]
                    (parameter_name, parameter_value, category, display_name, description, display_order, value_type)
                VALUES
                    ('EnableConfigurationRollout', 'true',  'FeatureFlag', 'Enable Configuration Rollout',
                     'Enables the configuration rollout engine. When false, rollout requests are accepted but not dispatched.',
                     10, 'Boolean'),
                    ('EnableTopologyEditing',      'false', 'FeatureFlag', 'Enable Topology Editing',
                     'Allows operators to modify topology edges (channels, routers) from the UI.',
                     20, 'Boolean'),
                    ('EnableExperimentalUI',       'false', 'FeatureFlag', 'Enable Experimental UI',
                     'Shows experimental dashboard panels and UI features not yet promoted to stable.',
                     30, 'Boolean'),
                    ('EnableBackgroundCleanup',    'true',  'FeatureFlag', 'Enable Background Cleanup',
                     'Enables the background worker that purges expired export jobs and old operation records.',
                     40, 'Boolean'),
                    ('EnableExportJobs',           'true',  'FeatureFlag', 'Enable Export Jobs',
                     'Enables the export job subsystem. When false, POST /export-jobs returns 503.',
                     50, 'Boolean');");

            // ── Seed: Retention Policies ───────────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO [msosync].[sync_parameter]
                    (parameter_name, parameter_value, category, display_name, description, display_order, value_type, minimum_value, maximum_value)
                VALUES
                    ('Retention.AuditDays',              '90',  'Retention', 'Audit Log Retention (days)',
                     'Number of days to retain audit log entries. Entries older than this are purged by the background cleanup worker.',
                     110, 'Integer', '1', '3650'),
                    ('Retention.OperationDays',          '180', 'Retention', 'Operation Record Retention (days)',
                     'Number of days to retain completed/failed operation records in sync_operation.',
                     120, 'Integer', '1', '3650'),
                    ('Retention.ConnectivityHistoryDays','30',  'Retention', 'Connectivity History Retention (days)',
                     'Number of days to retain node connectivity history rows.',
                     130, 'Integer', '1', '365'),
                    ('Retention.LifecycleHistoryDays',   '365', 'Retention', 'Lifecycle History Retention (days)',
                     'Number of days to retain node lifecycle transition history rows.',
                     140, 'Integer', '1', '3650'),
                    ('Retention.ExportJobHours',         '24',  'Retention', 'Export Job Retention (hours)',
                     'Number of hours a completed or failed export job file is retained before expiry.',
                     150, 'Integer', '1', '720');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove seed data first
            migrationBuilder.Sql(@"
                DELETE FROM [msosync].[sync_parameter]
                WHERE parameter_name IN (
                    'EnableConfigurationRollout', 'EnableTopologyEditing', 'EnableExperimentalUI',
                    'EnableBackgroundCleanup', 'EnableExportJobs',
                    'Retention.AuditDays', 'Retention.OperationDays', 'Retention.ConnectivityHistoryDays',
                    'Retention.LifecycleHistoryDays', 'Retention.ExportJobHours'
                );");

            // Drop metadata columns
            migrationBuilder.DropColumn(name: "conflicts_with",  schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "depends_on",      schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "allowed_values",  schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "maximum_value",   schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "minimum_value",   schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "value_type",      schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "display_order",   schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "description",     schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "display_name",    schema: "msosync", table: "sync_parameter");
            migrationBuilder.DropColumn(name: "category",        schema: "msosync", table: "sync_parameter");
        }
    }
}
