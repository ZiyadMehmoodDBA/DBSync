using MSOSync.Persistence.Entities;

namespace MSOSync.Event;

public interface IEventReader
{
    Task<IReadOnlyList<SyncDataEvent>> ReadAsync(int batchSize, CancellationToken ct = default);
}
