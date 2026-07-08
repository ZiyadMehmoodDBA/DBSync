using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public interface IDriftDetector
{
    ConfigurationState Compute(SyncNode node, DateTime nowUtc, int heartbeatIntervalSeconds, int missedThreshold);
}
