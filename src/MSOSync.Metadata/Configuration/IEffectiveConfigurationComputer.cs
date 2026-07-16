using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Models;

namespace MSOSync.Metadata.Configuration;

public sealed record EffectiveConfigResult(ConfigurationSettings Settings, string EffectiveHash);

public interface IEffectiveConfigurationComputer
{
    Task<EffectiveConfigResult> ComputeAsync(
        SyncConfigurationTemplateVersion version, string nodeId, CancellationToken ct);
}
