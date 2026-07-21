namespace MSOSync.Metadata.Operations;

public enum OperationType
{
    Export,
    Rollout,
    Decommission,
    Recovery,
    RollingMaintenance,
    RollingUpgrade,
}

public enum OperationSource
{
    User,
    System,
    Scheduler,
    Worker,
    Api,
}

public enum OperationStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public enum OperationResult
{
    Success,
    PartialSuccess,
    Failure,
    Cancelled,
}
