using MediatR;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Events;

public sealed record NodeLifecycleChangedEvent(
    string NodeId,
    NodeLifecycleState PreviousState,
    NodeLifecycleState NewState,
    LifecycleTrigger Trigger,
    Guid CorrelationId) : INotification;
