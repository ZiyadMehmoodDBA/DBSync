using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeBootstrapTokenConfiguration : IEntityTypeConfiguration<SyncNodeBootstrapToken>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeBootstrapToken> builder)
    {
        builder.ToTable("sync_node_bootstrap_token", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(e => e.NodeId).HasColumnName("node_id")
            .HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.TokenHash).HasColumnName("token_hash")
            .HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.IssuedBy).HasColumnName("issued_by")
            .HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.HasOne<SyncNode>().WithMany().HasForeignKey(e => e.NodeId)
            .HasConstraintName("FK_node_bootstrap_token_node").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => e.NodeId).HasDatabaseName("IX_node_bootstrap_token_node");

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.NodeId })
            .HasDatabaseName("IX_sync_node_bootstrap_token_TenantId_NodeId");
    }
}
