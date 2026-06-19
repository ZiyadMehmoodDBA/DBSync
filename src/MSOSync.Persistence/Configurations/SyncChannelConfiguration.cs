using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncChannelConfiguration : IEntityTypeConfiguration<SyncChannel>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncChannel> builder)
    {
        builder.ToTable("sync_channel", Schema);
        builder.HasKey(e => e.ChannelId);

        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.Priority).HasColumnName("priority").IsRequired();
        builder.Property(e => e.BatchSize).HasColumnName("batch_size").HasDefaultValue(1000);
        builder.Property(e => e.MaxBatchToSend).HasColumnName("max_batch_to_send").HasDefaultValue(10);
        builder.Property(e => e.MaxDataSize).HasColumnName("max_data_size").HasDefaultValue(1048576L);
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
    }
}
