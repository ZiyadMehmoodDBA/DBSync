using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncParameterHistConfiguration : IEntityTypeConfiguration<SyncParameterHist>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncParameterHist> builder)
    {
        builder.ToTable("sync_parameter_hist", Schema);
        builder.HasKey(e => e.HistId);

        builder.Property(e => e.HistId).HasColumnName("hist_id").ValueGeneratedOnAdd();
        builder.Property(e => e.ParameterName).HasColumnName("parameter_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.OldValue).HasColumnName("old_value").HasColumnType("nvarchar(max)");
        builder.Property(e => e.NewValue).HasColumnName("new_value").HasColumnType("nvarchar(max)");
        builder.Property(e => e.ChangedBy).HasColumnName("changed_by").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.ChangeTime).HasColumnName("change_time").HasColumnType("datetime2(7)");
    }
}
