using MediatR;
using MSOSync.Persistence;

namespace MSOSync.Scheduler;

public sealed record NodeConnectivityChangedEvent(
    string NodeId,
    ConnectivityStatus PreviousStatus,
    ConnectivityStatus NewStatus) : INotification;
