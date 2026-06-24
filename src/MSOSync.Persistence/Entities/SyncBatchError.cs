namespace MSOSync.Persistence.Entities;

public sealed class SyncBatchError
{
    public long      ErrorId       { get; set; }
    public long      BatchId       { get; set; }
    public long?     EventId       { get; set; }
    public string?   ConflictType  { get; set; }
    public string?   ErrorMessage  { get; set; }
    public int       RetryCount    { get; set; } = 0;
    public DateTime? LastRetryTime { get; set; }
    public DateTime  CreateTime    { get; set; }
}
