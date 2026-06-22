// tests/MSOSync.EngineTests/FakeClock.cs
using MSOSync.Common;

namespace MSOSync.EngineTests;

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan ts) => UtcNow += ts;
}
