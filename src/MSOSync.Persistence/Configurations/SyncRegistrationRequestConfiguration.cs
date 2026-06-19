using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRegistrationRequestConfiguration : IEntityTypeConfiguration<SyncRegistrationRequest>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRegistrationRequest> builder)
    {
        builder.ToTable("sync_registration_request", Schema);
        builder.HasKey(e => e.RequestId);

        builder.Property(e => e.RequestId).HasColumnName("request_id").ValueGeneratedOnAdd();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.NodeGroup).HasColumnName("node_group").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.SyncUrl).HasColumnName("sync_url").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false);
        builder.Property(e => e.NodeVersion).HasColumnName("node_version").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.DbType).HasColumnName("db_type").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.RequestTime).HasColumnName("request_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.Approved).HasColumnName("approved").HasDefaultValue(false);
    }
}
