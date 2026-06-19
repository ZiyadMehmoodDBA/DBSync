using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncDataEventBatchConfiguration : IEntityTypeConfiguration<SyncDataEventBatch>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncDataEventBatch> builder)
    {
        builder.ToTable("sync_data_event_batch", Schema);
        builder.HasKey(e => new { e.EventId, e.BatchId });

        builder.Property(e => e.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(e => e.BatchId).HasColumnName("batch_id").ValueGeneratedNever();
    }
}
