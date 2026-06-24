namespace MSOSync.Metadata.BatchErrors;

public sealed class BatchErrorFilter
{
    public long?          BatchId      { get; set; }
    public string?        ConflictType { get; set; }
    public ErrorSeverity? Severity     { get; set; }
    public DateTime?      From         { get; set; }
    public DateTime?      To           { get; set; }
    public int            Page         { get; set; } = 1;
    public int            PageSize     { get; set; } = 50;
}
