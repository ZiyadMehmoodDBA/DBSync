using MSOSync.Persistence.Entities;

namespace MSOSync.Batch;

public interface IBatchCreator
{
    Task<IReadOnlyList<SyncOutgoingBatch>> CreateBatchesAsync(
        IReadOnlyList<SyncDataEvent> events,
        IReadOnlyDictionary<long, IReadOnlyList<string>> routes,
        CancellationToken ct = default);
}
