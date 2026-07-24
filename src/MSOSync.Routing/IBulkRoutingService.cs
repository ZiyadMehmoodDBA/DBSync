namespace MSOSync.Routing;

/// <summary>
/// Resolves target nodes for a trigger and bulk-inserts one SyncOutgoingBatch row
/// per eligible node in a single SQL round-trip using INSERT … SELECT … OUTPUT.
/// </summary>
public interface IBulkRoutingService
{
    /// <summary>
    /// Inserts one outgoing batch row per eligible target node for the given trigger.
    /// Returns the list of <c>batch_id</c> identity values assigned by SQL Server.
    /// Returns an empty list when no eligible nodes are found.
    /// </summary>
    /// <param name="triggerId">The trigger whose router-node resolution determines target nodes.</param>
    /// <param name="channelId">The channel to record on each inserted batch row.</param>
    /// <param name="batchSequence">The batch sequence number shared across all inserted rows.</param>
    /// <param name="rowCount">Data row count to store on each batch row.</param>
    /// <param name="byteCount">Compressed byte count to store on each batch row.</param>
    /// <param name="tenantId">The tenant scope for both the node lookup and the insert.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<long>> FanOutAsync(
        string            triggerId,
        string            channelId,
        long              batchSequence,
        int               rowCount,
        long              byteCount,
        Guid              tenantId,
        CancellationToken ct = default);
}
