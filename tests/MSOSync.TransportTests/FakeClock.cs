using MSOSync.Common;

namespace MSOSync.TransportTests;

internal sealed class FakeClock(DateTime? utcNow = null) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow ?? DateTime.UtcNow;
}
