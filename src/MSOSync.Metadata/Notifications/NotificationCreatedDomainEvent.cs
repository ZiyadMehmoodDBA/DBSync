using MediatR;

namespace MSOSync.Metadata.Notifications;

public sealed record NotificationCreatedDomainEvent(
    long                              NotificationId,
    IReadOnlyList<long>               UserIds,
    NotificationPushDto               PushDto,
    IReadOnlyDictionary<long, int>    UnreadCounts) : INotification;
