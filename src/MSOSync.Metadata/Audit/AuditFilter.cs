namespace MSOSync.Metadata.Audit;

public sealed class AuditFilter
{
    public string?   Username   { get; set; }
    public string?   ActionName { get; set; }
    public DateTime? From       { get; set; }
    public DateTime? To         { get; set; }
    public int       Page       { get; set; } = 1;
    public int       PageSize   { get; set; } = 50;
}
