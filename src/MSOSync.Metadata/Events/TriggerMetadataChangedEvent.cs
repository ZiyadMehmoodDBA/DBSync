using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record TriggerMetadataChangedEvent(
    string TriggerId,
    string Action) : INotification;
