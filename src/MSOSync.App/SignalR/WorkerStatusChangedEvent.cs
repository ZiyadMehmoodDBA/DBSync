using MediatR;
using MSOSync.App.Workers;

namespace MSOSync.App.SignalR;

public sealed record WorkerStatusChangedEvent(
    string WorkerName,
    WorkerHealthState PreviousState,
    WorkerHealthState NewState,
    DateTime OccurredAt) : INotification;
