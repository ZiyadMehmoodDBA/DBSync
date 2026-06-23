using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeConfiguration : IEntityTypeConfiguration<SyncNode>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNode> builder)
    {
        builder.ToTable("sync_node", Schema);
        builder.HasKey(e => e.NodeId);

        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.GroupId).HasColumnName("group_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.SyncUrl).HasColumnName("sync_url").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.RegistrationTime).HasColumnName("registration_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.LastHeartbeat).HasColumnName("last_heartbeat").HasColumnType("datetime2(7)");
        builder.Property(e => e.HeartbeatInterval).HasColumnName("heartbeat_interval").HasDefaultValue(60);
        builder.Property(e => e.SyncEnabled).HasColumnName("sync_enabled").HasDefaultValue(true);
        builder.Property(e => e.TransportMode)
            .HasColumnName("transport_mode")
            .HasColumnType("tinyint")
            .HasConversion<byte>()
            .HasDefaultValue(TransportMode.Pull)
            .ValueGeneratedNever()
            .IsRequired();

        builder.HasIndex(e => e.LastHeartbeat).HasDatabaseName("IX_sync_node_last_heartbeat");

        builder.Property(e => e.UpstreamNodeId)
            .HasColumnName("upstream_node_id")
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100);

        builder.Property(e => e.LastProbeTime)
            .HasColumnName("last_probe_time")
            .HasColumnType("datetime2(7)");

        builder.Property(e => e.LastProbeLatencyMs)
            .HasColumnName("last_probe_latency_ms");

        builder.Property(e => e.ConnectivityStatus)
            .HasColumnName("connectivity_status")
            .HasColumnType("tinyint")
            .HasConversion<byte>()
            .HasDefaultValue(ConnectivityStatus.Unknown)
            .ValueGeneratedNever();

        builder.HasOne<SyncNode>()
            .WithMany()
            .HasForeignKey(e => e.UpstreamNodeId)
            .HasConstraintName("FK_sync_node_upstream_node_id")
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired(false);

        builder.HasIndex(e => e.UpstreamNodeId)
            .HasDatabaseName("IX_sync_node_upstream");
    }
}
