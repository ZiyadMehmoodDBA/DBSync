namespace MSOSync.Persistence.Entities;

public sealed class SyncParameterHist
{
    public long HistId { get; set; }
    public string ParameterName { get; set; } = null!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime? ChangeTime { get; set; }
}
