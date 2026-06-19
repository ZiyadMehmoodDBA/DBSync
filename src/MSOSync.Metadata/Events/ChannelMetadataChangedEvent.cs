using MediatR;

namespace MSOSync.Metadata.Events;

public sealed record ChannelMetadataChangedEvent(
    string ChannelId,
    string Action) : INotification;
