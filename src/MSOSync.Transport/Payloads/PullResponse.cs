using MSOSync.Engine;

namespace MSOSync.Transport.Payloads;

public sealed record PullResponse(
    IReadOnlyList<BatchPayload> Batches,
    bool                        MoreAvailable);
