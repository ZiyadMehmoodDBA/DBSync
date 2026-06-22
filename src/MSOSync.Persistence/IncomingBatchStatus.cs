namespace MSOSync.Persistence;

public enum IncomingBatchStatus : byte
{
    New           = 0,
    Applying      = 1,
    Applied       = 2,
    Error         = 3,
    PartialSuccess = 4
}
