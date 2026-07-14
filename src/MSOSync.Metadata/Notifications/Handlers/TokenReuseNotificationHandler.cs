using MediatR;
using MSOSync.Security.Events;

namespace MSOSync.Metadata.Notifications.Handlers;

public sealed class TokenReuseNotificationHandler(INotificationService svc)
    : INotificationHandler<TokenReuseDetectedEvent>
{
    public async Task Handle(TokenReuseDetectedEvent n, CancellationToken ct)
    {
        await svc.CreateAsync(
            NotificationEventType.TokenReuseDetected, NotificationSeverity.Security,
            "Token reuse detected",
            $"A previously consumed refresh token was reused for user '{n.Username}'. All tokens have been revoked.",
            null, null, n.CorrelationId,
            NotificationAudience.Administrators, ct);
    }
}
