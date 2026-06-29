namespace MSOSync.Metadata.Dashboard;

public sealed class ActivityFilter
{
    public DateTime? From  { get; set; }
    public DateTime? To    { get; set; }
    public int       Limit { get; set; } = 50;
    public string?   Type  { get; set; }
}
