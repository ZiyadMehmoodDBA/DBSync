using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncParameterConfiguration : IEntityTypeConfiguration<SyncParameter>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncParameter> builder)
    {
        builder.ToTable("sync_parameter", Schema);
        builder.HasKey(e => e.ParameterName);

        builder.Property(e => e.ParameterName).HasColumnName("parameter_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.ParameterValue).HasColumnName("parameter_value").HasColumnType("nvarchar(max)");
    }
}
