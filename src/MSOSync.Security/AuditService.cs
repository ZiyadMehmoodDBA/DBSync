using MediatR;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security.Events;

namespace MSOSync.Security;

public sealed class AuditService(AppDbContext db) :
    INotificationHandler<LoginSuccessEvent>,
    INotificationHandler<LoginFailureEvent>,
    INotificationHandler<AccountLockedEvent>,
    INotificationHandler<TokenReuseDetectedEvent>
{
    public Task Handle(LoginSuccessEvent notification, CancellationToken ct) =>
        WriteAuditAsync("LOGIN_SUCCESS", notification.Username, null, notification.CorrelationId, ct);

    public Task Handle(LoginFailureEvent notification, CancellationToken ct) =>
        WriteAuditAsync("LOGIN_FAILURE", notification.Username, null, notification.CorrelationId, ct);

    public Task Handle(AccountLockedEvent notification, CancellationToken ct) =>
        WriteAuditAsync("ACCOUNT_LOCKED", notification.Username, null, notification.CorrelationId, ct);

    public Task Handle(TokenReuseDetectedEvent notification, CancellationToken ct) =>
        WriteAuditAsync("TOKEN_REUSE_DETECTED", notification.Username,
            $"family:{notification.FamilyId}", notification.CorrelationId, ct);

    private async Task WriteAuditAsync(
        string actionName, string? username, string? objectName,
        string? correlationId, CancellationToken ct)
    {
        db.Audits.Add(new SyncAudit
        {
            ActionName = actionName,
            Username = username,
            ObjectName = objectName,
            CorrelationId = correlationId,
            CreateTime = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
