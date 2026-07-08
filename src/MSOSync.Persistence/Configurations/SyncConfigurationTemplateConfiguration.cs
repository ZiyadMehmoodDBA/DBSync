using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncConfigurationTemplateConfiguration : IEntityTypeConfiguration<SyncConfigurationTemplate>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncConfigurationTemplate> builder)
    {
        builder.ToTable("sync_configuration_template", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("NEWID()");
        builder.Property(e => e.Name).HasColumnName("name")
            .HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description")
            .HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.Status).HasColumnName("status")
            .HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.CurrentPublishedVersion).HasColumnName("current_published_version");
        builder.Property(e => e.LatestDraftVersion).HasColumnName("latest_draft_version");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => e.Name).IsUnique().HasDatabaseName("UX_sync_configuration_template_name");

        builder.HasMany(e => e.Versions).WithOne(v => v.Template)
            .HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Cascade);
    }
}
