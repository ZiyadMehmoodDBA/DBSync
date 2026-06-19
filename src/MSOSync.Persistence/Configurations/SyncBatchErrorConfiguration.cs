using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncBatchErrorConfiguration : IEntityTypeConfiguration<SyncBatchError>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncBatchError> builder)
    {
        builder.ToTable("sync_batch_error", Schema);
        builder.HasKey(e => e.ErrorId);

        builder.Property(e => e.ErrorId).HasColumnName("error_id").ValueGeneratedOnAdd();
        builder.Property(e => e.BatchId).HasColumnName("batch_id").IsRequired();
        builder.Property(e => e.EventId).HasColumnName("event_id");
        builder.Property(e => e.ConflictType).HasColumnName("conflict_type").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("nvarchar(max)");
        builder.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        builder.Property(e => e.LastRetryTime).HasColumnName("last_retry_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => e.BatchId).HasDatabaseName("IX_sync_batch_error_batch_id");

        builder.HasOne<SyncOutgoingBatch>()
            .WithMany()
            .HasForeignKey(e => e.BatchId)
            .HasConstraintName("FK_sync_batch_error_batch_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
