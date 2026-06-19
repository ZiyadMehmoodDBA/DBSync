using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record NodeMetadataChangedEvent(
    string NodeId,
    string Action) : INotification;
