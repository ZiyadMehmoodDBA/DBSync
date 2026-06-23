namespace MSOSync.Engine;

public interface ITriggerApplyMetadataService
{
    Task<Dictionary<string, TriggerApplyMetadata>> GetMetadataAsync(
        IReadOnlyList<string> triggerIds,
        CancellationToken     ct = default);
}
