namespace MSOSync.Metadata.Audit;

public sealed class AuditFilter
{
    public string?   Username          { get; set; }
    public string?   ActionName        { get; set; }
    public DateTime? From              { get; set; }
    public DateTime? To                { get; set; }
    public string?   Cursor            { get; set; }
    public bool      IncludeTotalCount { get; set; }
    public int       PageSize          { get; set; } = 50;
}
