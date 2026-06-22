namespace MSOSync.Batch;

public interface IBatchStateMachine
{
    bool CanTransition(BatchStatus from, BatchStatus to);
    Task<bool> TransitionAsync(long batchId, BatchStatus from, BatchStatus to, CancellationToken ct = default);
}
