namespace MSOSync.Common;

public interface IClock
{
    DateTime UtcNow { get; }
}
