namespace MSOSync.Batch;

public enum BatchStatus : byte
{
    New          = 0,
    Sending      = 1,   // PUSH: HTTP call in-flight; crash → Sending→Error in SchedulerRecovery
    Acknowledged = 2,
    Error        = 3,
    Retry        = 4
}
