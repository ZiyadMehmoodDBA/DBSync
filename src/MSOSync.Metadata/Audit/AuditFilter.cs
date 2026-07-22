namespace MSOSync.Metadata.Audit;

public sealed class AuditFilter
{
    // Existing single-value fields — kept for backward compatibility
    public string?   Username          { get; set; }
    public string?   ActionName        { get; set; }
    // New multi-value fields (take precedence when non-empty)
    public string[]? Usernames         { get; set; }   // OR within group
    public string[]? ActionNames       { get; set; }   // OR within group
    public string[]? ObjectNames       { get; set; }   // OR within group
    // Existing
    public DateTime? From              { get; set; }
    public DateTime? To                { get; set; }
    public string?   Cursor            { get; set; }
    public bool      IncludeTotalCount { get; set; }
    public int       PageSize          { get; set; } = 50;
}
