namespace MSOSync.Persistence.Entities;

public sealed class SyncUserPreference
{
    public long     PreferenceId    { get; set; }
    public long     UserId          { get; set; }
    public string   PreferenceKey   { get; set; } = "";
    public string   PreferenceValue { get; set; } = "";
    public DateTime UpdatedAt       { get; set; }

    public SyncUser User { get; set; } = null!;
}
