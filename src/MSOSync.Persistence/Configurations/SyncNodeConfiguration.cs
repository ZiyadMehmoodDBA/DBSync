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
        builder.Property(e => e.LifecycleState)
            .HasColumnName("status")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(e => e.RegistrationTime).HasColumnName("registration_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.LastHeartbeat).HasColumnName("last_heartbeat").HasColumnType("datetime2(7)");
        builder.Property(e => e.HeartbeatInterval).HasColumnName("heartbeat_interval").HasDefaultValue(60);
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
            .HasColumnType("varchar(50)")
            .HasMaxLength(50)
            .IsUnicode(false);

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

        builder.Property(e => e.ConnectivityReason).HasColumnName("connectivity_reason")
            .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>();
        builder.Property(e => e.LastProbeError).HasColumnName("last_probe_error")
            .HasColumnType("nvarchar(512)").HasMaxLength(512);
        builder.Property(e => e.ConsecutiveProbeFailures).HasColumnName("consecutive_probe_failures")
            .HasDefaultValue(0);
        builder.Property(e => e.PreviousLifecycleState).HasColumnName("previous_lifecycle_state")
            .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>();
        builder.Property(e => e.MaintenanceMode).HasColumnName("maintenance_mode").HasDefaultValue(false);
        builder.Property(e => e.MaintenanceReason).HasColumnName("maintenance_reason")
            .HasColumnType("nvarchar(512)").HasMaxLength(512);
        builder.Property(e => e.MaintenanceStartedAt).HasColumnName("maintenance_started_at");
        builder.Property(e => e.MaintenanceUntil).HasColumnName("maintenance_until");
        builder.Property(e => e.MaintenanceStartedBy).HasColumnName("maintenance_started_by")
            .HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.DrainCompletedAt).HasColumnName("drain_completed_at");
        builder.Property(e => e.AgentVersion).HasColumnName("agent_version")
            .HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.DecommissionReason).HasColumnName("decommission_reason")
            .HasColumnType("nvarchar(512)").HasMaxLength(512);
        builder.Property(e => e.DecommissionStartedAt).HasColumnName("decommission_started_at");
        builder.Property(e => e.DecommissionGraceUntil).HasColumnName("decommission_grace_until");
        builder.Property(e => e.DecommissionInitialOpenBatches).HasColumnName("decommission_initial_open_batches");
        builder.Property(e => e.RowVersion).HasColumnName("row_version").IsRowVersion();
        builder.HasIndex(e => e.LifecycleState).HasDatabaseName("IX_sync_node_status");

        builder.Property(e => e.NodeType)
            .HasColumnName("node_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.ExternalId)
            .HasColumnName("external_id")
            .HasMaxLength(100)
            .IsRequired(false)
            .HasDefaultValue("");

        builder.Property(e => e.NodeName)
            .HasColumnName("node_name")
            .HasMaxLength(256)
            .IsRequired(false)
            .HasDefaultValue("");

        builder.Property(e => e.DbServer)
            .HasColumnName("db_server")
            .HasColumnType("nvarchar(255)")
            .HasMaxLength(255);

        builder.Property(e => e.DbName)
            .HasColumnName("db_name")
            .HasColumnType("nvarchar(255)")
            .HasMaxLength(255);

        builder.Property(e => e.DbAuthMode)
            .HasColumnName("db_auth_mode")
            .HasColumnType("varchar(10)")
            .HasMaxLength(10)
            .IsUnicode(false);

        builder.Property(e => e.DbUser)
            .HasColumnName("db_user")
            .HasColumnType("nvarchar(128)")
            .HasMaxLength(128);

        builder.Property(e => e.DbPasswordEncrypted)
            .HasColumnName("db_password_encrypted")
            .HasColumnType("nvarchar(1000)")
            .HasMaxLength(1000);

        builder.HasOne<SyncNode>()
            .WithMany()
            .HasForeignKey(e => e.UpstreamNodeId)
            .HasConstraintName("FK_sync_node_upstream_node_id")
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired(false);

        builder.HasIndex(e => e.UpstreamNodeId)
            .HasDatabaseName("IX_sync_node_upstream");

        // Epic 12B-2 — Configuration management (all nullable)
        builder.Property(e => e.AssignedTemplateId).HasColumnName("assigned_template_id");
        builder.Property(e => e.AssignedTemplateVersion).HasColumnName("assigned_template_version");
        builder.Property(e => e.AppliedTemplateVersion).HasColumnName("applied_template_version");
        builder.Property(e => e.ExpectedEffectiveHash).HasColumnName("expected_effective_hash")
            .HasColumnType("nvarchar(64)").HasMaxLength(64);
        builder.Property(e => e.AppliedEffectiveHash).HasColumnName("applied_effective_hash")
            .HasColumnType("nvarchar(64)").HasMaxLength(64);
        builder.Property(e => e.ConfigurationState).HasColumnName("configuration_state")
            .HasColumnType("nvarchar(20)").HasMaxLength(20).HasConversion<string>();
        builder.Property(e => e.ConfigurationStatusReportedAt).HasColumnName("configuration_status_reported_at");
        builder.Property(e => e.LastAppliedAt).HasColumnName("last_applied_at");

        // M031 — multi-tenancy
        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_sync_node_tenant_id");
    }
}
