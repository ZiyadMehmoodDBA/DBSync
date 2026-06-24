using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncDataEventConfiguration : IEntityTypeConfiguration<SyncDataEvent>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncDataEvent> builder)
    {
        builder.ToTable("sync_data_event", Schema);
        builder.HasKey(e => e.EventId);

        builder.Property(e => e.EventId).HasColumnName("event_id").ValueGeneratedOnAdd();
        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.SourceNodeId).HasColumnName("source_node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasColumnType("char(1)")
            .IsUnicode(false)
            .HasConversion(v => v.ToString(), v => v.Length > 0 ? v[0] : 'I');
        builder.Property(e => e.TableName).HasColumnName("table_name").HasColumnType("varchar(128)").HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(e => e.PkData).HasColumnName("pk_data").HasColumnType("nvarchar(max)");
        builder.Property(e => e.RowData).HasColumnName("row_data").HasColumnType("nvarchar(max)");
        builder.Property(e => e.TransactionId).HasColumnName("transaction_id");
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)").IsRequired();
        builder.Property(e => e.IsProcessed).HasColumnName("is_processed").HasDefaultValue(false);

        builder.HasIndex(e => new { e.ChannelId, e.IsProcessed }).HasDatabaseName("IX_sync_data_event_channel_processed");
        builder.HasIndex(e => e.TransactionId).HasDatabaseName("IX_sync_data_event_transaction_id");
        builder.HasIndex(e => e.CreateTime).HasDatabaseName("IX_sync_data_event_create_time");
        builder.HasIndex(e => e.SourceNodeId).HasDatabaseName("IX_sync_data_event_source_node_id");
        builder.HasIndex(e => e.TriggerId).HasDatabaseName("IX_sync_data_event_trigger_id");
        builder.HasIndex(e => new { e.ChannelId, e.CreateTime })
            .IsDescending(false, true)
            .HasDatabaseName("IX_sync_data_event_channel_time");
    }
}
