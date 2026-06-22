namespace MSOSync.Batch;

public enum BatchStatus : byte
{
    New   = 0,
    Sent  = 1,
    Ok    = 2,
    Error = 3,
    Retry = 4
}
