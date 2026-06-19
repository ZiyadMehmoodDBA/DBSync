using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncTriggerHistConfiguration : IEntityTypeConfiguration<SyncTriggerHist>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncTriggerHist> builder)
    {
        builder.ToTable("sync_trigger_hist", Schema);
        builder.HasKey(e => e.HistId);

        builder.Property(e => e.HistId).HasColumnName("hist_id").ValueGeneratedOnAdd();
        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.DdlText).HasColumnName("ddl_text").HasColumnType("nvarchar(max)");
        builder.Property(e => e.TriggerVersion).HasColumnName("trigger_version");
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");
    }
}
