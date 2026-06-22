// src/MSOSync.Trigger/ITriggerDriftDetector.cs
namespace MSOSync.Trigger;

public interface ITriggerDriftDetector
{
    Task DetectAllAsync(CancellationToken ct = default);
    Task<TriggerVerifyResult> VerifyAsync(string triggerId, CancellationToken ct = default);
}
