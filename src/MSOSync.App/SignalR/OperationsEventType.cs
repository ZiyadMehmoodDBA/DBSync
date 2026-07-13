namespace MSOSync.App.SignalR;

public enum OperationsEventType
{
    NodeHealthChanged,
    NodeApproved,
    NodeRejected,
    NodeDisabled,
    NodeEnabled,
    SyncCycleCompleted,
    NodeLifecycleChanged,
    NodeMaintenanceChanged,
    ConfigurationChanged,
    OperationChanged,           // Epic 12C — sync_operation state transitions
    WorkerStatusChanged,        // Epic 12C — worker health state transitions
}
