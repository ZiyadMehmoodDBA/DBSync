using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Routing;

/// <summary>
/// Implements <see cref="IBulkRoutingService"/> using a single parameterised
/// <c>INSERT INTO … SELECT … OUTPUT</c> SQL statement.
/// Registered as <b>scoped</b>. Do NOT use <c>Task.WhenAll</c> with the shared
/// <see cref="AppDbContext"/> — all operations on this context must be sequential.
/// </summary>
public sealed class BulkRoutingService(AppDbContext db) : IBulkRoutingService
{
    // NodeLifecycleState.Active is stored as the string "Active" in the [status] column
    // (configured via .HasConversion<string>() in SyncNodeConfiguration).
    private const string ActiveState = "Active";

    // INSERT ... OUTPUT ... SELECT syntax: OUTPUT clause comes between the column list
    // and the SELECT clause in SQL Server INSERT-SELECT statements.
    private const string FanOutSql = """
        INSERT INTO [msosync].[sync_outgoing_batch]
            ([batch_sequence], [node_id], [channel_id], [status],
             [row_count], [byte_count], [retry_count], [create_time], [tenant_id])
        OUTPUT INSERTED.[batch_id]
        SELECT
            @batchSequence,
            n.[node_id],
            @channelId,
            0,
            @rowCount,
            @byteCount,
            0,
            SYSUTCDATETIME(),
            @tenantId
        FROM [msosync].[sync_node] n
        INNER JOIN [msosync].[sync_trigger_router] tr
            ON tr.[trigger_id] = @triggerId
            AND tr.[enabled]   = 1
            AND tr.[tenant_id] = @tenantId
        INNER JOIN [msosync].[sync_router] r
            ON r.[router_id]          = tr.[router_id]
            AND r.[enabled]           = 1
            AND r.[target_node_group] = n.[group_id]
            AND r.[tenant_id]         = @tenantId
        WHERE n.[status]           = @activeState
          AND n.[maintenance_mode] = 0
          AND n.[tenant_id]        = @tenantId;
        """;

    public async Task<IReadOnlyList<long>> FanOutAsync(
        string            triggerId,
        string            channelId,
        long              batchSequence,
        int               rowCount,
        long              byteCount,
        Guid              tenantId,
        CancellationToken ct = default)
    {
        var batchIds = new List<long>();

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = FanOutSql;
        cmd.Parameters.Add(new SqlParameter("@triggerId",     triggerId));
        cmd.Parameters.Add(new SqlParameter("@channelId",     channelId));
        cmd.Parameters.Add(new SqlParameter("@batchSequence", batchSequence));
        cmd.Parameters.Add(new SqlParameter("@rowCount",      rowCount));
        cmd.Parameters.Add(new SqlParameter("@byteCount",     byteCount));
        cmd.Parameters.Add(new SqlParameter("@tenantId",      tenantId));
        cmd.Parameters.Add(new SqlParameter("@activeState",   ActiveState));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            batchIds.Add(reader.GetInt64(0));

        return batchIds.AsReadOnly();
    }
}
