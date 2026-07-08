namespace MSOSync.Persistence.Entities;

public sealed class SyncConfigurationTemplateVersion
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public bool IsDraft { get; set; }
    public string SettingsJson { get; set; } = null!;
    public string? TemplateContentHash { get; set; }            // null while draft
    public int SchemaVersion { get; set; } = 1;
    public byte[] RowVersion { get; set; } = [];
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }

    public SyncConfigurationTemplate Template { get; set; } = null!;
}
