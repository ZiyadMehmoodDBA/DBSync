using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncTriggerConfiguration : IEntityTypeConfiguration<SyncTrigger>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncTrigger> builder)
    {
        builder.ToTable("sync_trigger", Schema);
        builder.HasKey(e => e.TriggerId);

        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.SourceTable).HasColumnName("source_table").HasColumnType("varchar(128)").HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.SyncOnInsert).HasColumnName("sync_on_insert").HasDefaultValue(true);
        builder.Property(e => e.SyncOnUpdate).HasColumnName("sync_on_update").HasDefaultValue(true);
        builder.Property(e => e.SyncOnDelete).HasColumnName("sync_on_delete").HasDefaultValue(true);
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
        builder.Property(e => e.TriggerVersion).HasColumnName("trigger_version").HasDefaultValue(0);
        builder.Property(e => e.LastVerifiedTime).HasColumnName("last_verified_time").HasColumnType("datetime2(7)");
    }
}
