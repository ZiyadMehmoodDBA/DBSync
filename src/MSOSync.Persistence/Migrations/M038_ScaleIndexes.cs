using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M038_ScaleIndexes : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Index 1: Covers EventQueryService correlated MAX(batch_id) subquery.
        // The existing composite PK (event_id, batch_id) is not efficiently used
        // by the nested-loop lookup EF generates; a dedicated single-column index
        // on event_id gives a clean seek.
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_data_event_batch_event_id'
                  AND object_id = OBJECT_ID('[msosync].[sync_data_event_batch]'))
            CREATE INDEX [IX_sync_data_event_batch_event_id]
                ON [msosync].[sync_data_event_batch] ([event_id] ASC);
        ");

        // Index 2: Covers GetTopologySummaryAsync status-bucket counts and
        // DashboardSummaryDto reachability counts (GROUP BY connectivity_status).
        // INCLUDE adds lifecycle_state + maintenance_mode so ClusterSummaryQueryService
        // projection (SELECT lifecycle_state, maintenance_mode) becomes a covering scan.
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_connectivity_status'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            CREATE INDEX [IX_sync_node_connectivity_status]
                ON [msosync].[sync_node] ([connectivity_status] ASC)
                INCLUDE ([status], [maintenance_mode]);
        ");

        // Index 3: Covers GetGroupNodesAsync (filter by group_id, ORDER BY node_id
        // for cursor pagination). INCLUDE adds status fields used in
        // TopologyGroupNodeDto projection for a covering scan.
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_group_id'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            CREATE INDEX [IX_sync_node_group_id]
                ON [msosync].[sync_node] ([group_id] ASC, [node_id] ASC)
                INCLUDE ([status], [connectivity_status]);
        ");

        // Index 4: Covers dashboard and metrics queries that filter outgoing batches
        // by time window. Existing IX_sync_outgoing_batch_node_status covers per-node
        // status lookups; this index covers time-range queries on create_time.
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_outgoing_batch_create_time'
                  AND object_id = OBJECT_ID('[msosync].[sync_outgoing_batch]'))
            CREATE INDEX [IX_sync_outgoing_batch_create_time]
                ON [msosync].[sync_outgoing_batch] ([create_time] DESC)
                INCLUDE ([node_id], [channel_id], [status]);
        ");

        // Index 5: Covers IBulkRoutingService.FanOutAsync WHERE status = 'Active'
        // predicate and NodeSyncPolicy.EligibleExpression used by RoutingService.ResolveAsync.
        // INCLUDE adds group_id (JOIN predicate), maintenance_mode (eligibility filter),
        // tenant_id (isolation predicate).
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_lifecycle_state'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            CREATE INDEX [IX_sync_node_lifecycle_state]
                ON [msosync].[sync_node] ([status] ASC)
                INCLUDE ([group_id], [maintenance_mode], [tenant_id]);
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_data_event_batch_event_id'
                  AND object_id = OBJECT_ID('[msosync].[sync_data_event_batch]'))
            DROP INDEX [IX_sync_data_event_batch_event_id]
                ON [msosync].[sync_data_event_batch];
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_connectivity_status'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            DROP INDEX [IX_sync_node_connectivity_status]
                ON [msosync].[sync_node];
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_group_id'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            DROP INDEX [IX_sync_node_group_id]
                ON [msosync].[sync_node];
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_outgoing_batch_create_time'
                  AND object_id = OBJECT_ID('[msosync].[sync_outgoing_batch]'))
            DROP INDEX [IX_sync_outgoing_batch_create_time]
                ON [msosync].[sync_outgoing_batch];
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_lifecycle_state'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            DROP INDEX [IX_sync_node_lifecycle_state]
                ON [msosync].[sync_node];
        ");
    }
}
