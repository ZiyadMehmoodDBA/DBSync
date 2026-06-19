using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record RouterMetadataChangedEvent(
    string RouterId,
    string Action) : INotification;
