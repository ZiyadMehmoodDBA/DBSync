using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncConfigurationTemplateVersionConfiguration
    : IEntityTypeConfiguration<SyncConfigurationTemplateVersion>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncConfigurationTemplateVersion> builder)
    {
        builder.ToTable("sync_configuration_template_version", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("NEWID()");
        builder.Property(e => e.TemplateId).HasColumnName("template_id").IsRequired();
        builder.Property(e => e.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(e => e.IsDraft).HasColumnName("is_draft").IsRequired();
        builder.Property(e => e.SettingsJson).HasColumnName("settings_json")
            .HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(e => e.TemplateContentHash).HasColumnName("template_content_hash")
            .HasColumnType("nvarchar(64)").HasMaxLength(64);
        builder.Property(e => e.SchemaVersion).HasColumnName("schema_version").HasDefaultValue(1);
        builder.Property(e => e.RowVersion).HasColumnName("row_version").IsRowVersion();
        builder.Property(e => e.PublishedAt).HasColumnName("published_at");
        builder.Property(e => e.PublishedBy).HasColumnName("published_by");

        // Unique version number per template
        builder.HasIndex(e => new { e.TemplateId, e.VersionNumber }).IsUnique()
            .HasDatabaseName("UX_template_version_number");

        // Filtered unique: at most one draft per template
        builder.HasIndex(e => e.TemplateId)
            .IsUnique()
            .HasFilter("[is_draft] = 1")
            .HasDatabaseName("UX_template_single_draft");
    }
}
