using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncConfigurationTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = "Draft";               // Draft / Published / Archived
    public int? CurrentPublishedVersion { get; set; }
    public int? LatestDraftVersion { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<SyncConfigurationTemplateVersion> Versions { get; set; } = [];
}
