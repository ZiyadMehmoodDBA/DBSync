// src/MSOSync.Persistence/Migrations/M008_SeedData.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000008_SeedData")]
public partial class M008_SeedData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Roles
        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_role] WHERE [role_name] = 'ADMIN')
    INSERT INTO [msosync].[sync_role] ([role_name]) VALUES ('ADMIN');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_role] WHERE [role_name] = 'OPERATOR')
    INSERT INTO [msosync].[sync_role] ([role_name]) VALUES ('OPERATOR');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_role] WHERE [role_name] = 'VIEWER')
    INSERT INTO [msosync].[sync_role] ([role_name]) VALUES ('VIEWER');
");

        // Default channel
        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_channel] WHERE [channel_id] = 'config')
    INSERT INTO [msosync].[sync_channel]
        ([channel_id], [priority], [batch_size], [max_batch_to_send], [max_data_size], [enabled])
    VALUES ('config', 100, 1000, 10, 1048576, 1);
");

        // Default parameters
        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'sync.interval.seconds')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('sync.interval.seconds', '900');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'retention.days')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('retention.days', '30');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'audit.retention.days')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('audit.retention.days', '90');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'max.retries')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('max.retries', '3');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'heartbeat.interval.seconds')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('heartbeat.interval.seconds', '60');
IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_parameter] WHERE [parameter_name] = 'queue.warn.threshold')
    INSERT INTO [msosync].[sync_parameter] ([parameter_name], [parameter_value]) VALUES ('queue.warn.threshold', '0.8');
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DELETE FROM [msosync].[sync_parameter]
WHERE [parameter_name] IN (
    'sync.interval.seconds','retention.days','audit.retention.days',
    'max.retries','heartbeat.interval.seconds','queue.warn.threshold');
DELETE FROM [msosync].[sync_channel] WHERE [channel_id] = 'config';
DELETE FROM [msosync].[sync_role] WHERE [role_name] IN ('ADMIN','OPERATOR','VIEWER');
");
    }
}
