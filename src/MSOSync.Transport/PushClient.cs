using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

/// <summary>
/// Stub — Task 7 implements HTTP push logic.
/// </summary>
public sealed class PushClient
{
    /// <summary>Stub — Task 7 fills in HTTP dispatch.</summary>
    public Task<PushResponse> PushAsync(
        string                       syncUrl,
        SyncOutgoingBatch            batch,
        IReadOnlyList<SyncDataEvent> events,
        CancellationToken            ct = default)
        => throw new NotImplementedException("PushClient is a Task 7 stub.");
}
