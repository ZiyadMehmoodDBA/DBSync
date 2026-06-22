// src/MSOSync.Trigger/ITriggerInstallationService.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Trigger;

public interface ITriggerInstallationService
{
    Task<TriggerVerifyResult> InstallAsync(SyncTrigger trigger, string nodeId, CancellationToken ct = default);
    Task DropAsync(string triggerId, CancellationToken ct = default);
    Task<TriggerVerifyResult> RebuildAsync(string triggerId, CancellationToken ct = default);
}
