namespace MSOSync.Metadata.Operations.Rolling;

public enum RollingStepStatus
{
    Pending,
    Draining,
    InMaintenance,
    AwaitingVerification,
    Completed,
    Failed,
    Skipped,
}
