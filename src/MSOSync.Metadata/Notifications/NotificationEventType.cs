namespace MSOSync.Metadata.Notifications;

public enum NotificationEventType
{
    WorkerFailed,
    WorkerWarning,
    NodeUnreachable,
    NodeInRecovery,
    NodeRejected,
    NodeDecommissioned,
    SchedulerRecovered,
    AccountLocked,
    TokenReuseDetected,
    OperationFailed
}
