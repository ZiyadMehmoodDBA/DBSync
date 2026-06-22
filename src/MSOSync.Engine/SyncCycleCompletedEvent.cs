using MediatR;

namespace MSOSync.Engine;

public sealed record SyncCycleCompletedEvent(
    int EventsRead,
    int BatchesCreated,
    TimeSpan Duration) : INotification;
