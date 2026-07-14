using MediatR;
using MSOSync.Security.Events;

namespace MSOSync.Metadata.Notifications.Handlers;

public sealed class AccountLockedNotificationHandler(INotificationService svc)
    : INotificationHandler<AccountLockedEvent>
{
    public async Task Handle(AccountLockedEvent n, CancellationToken ct)
    {
        await svc.CreateAsync(
            NotificationEventType.AccountLocked, NotificationSeverity.Security,
            $"Account '{n.Username}' locked",
            $"Account {n.Username} was locked due to repeated failed login attempts.",
            null, null, n.CorrelationId,
            NotificationAudience.Administrators, ct);
    }
}
