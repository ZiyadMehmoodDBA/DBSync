namespace MSOSync.Batch;

public interface IBatchStateMachine
{
    Task<bool> MoveToSendingAsync(long batchId, CancellationToken ct = default);
    Task<bool> MoveToAcknowledgedAsync(long batchId, DateTimeOffset ackTime, CancellationToken ct = default);
    Task<bool> MoveToErrorAsync(long batchId, CancellationToken ct = default);
    Task<bool> MoveToRetryAsync(long batchId, CancellationToken ct = default);
}
