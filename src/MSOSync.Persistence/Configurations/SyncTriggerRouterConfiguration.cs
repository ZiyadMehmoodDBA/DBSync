using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncTriggerRouterConfiguration : IEntityTypeConfiguration<SyncTriggerRouter>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncTriggerRouter> builder)
    {
        builder.ToTable("sync_trigger_router", Schema);
        builder.HasKey(e => new { e.TriggerId, e.RouterId });

        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.RouterId).HasColumnName("router_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
    }
}
