namespace MSOSync.Persistence;

public enum ConnectivityStatus : byte
{
    Unknown     = 0,
    Reachable   = 1,
    Degraded    = 2,
    Unreachable = 3
}
