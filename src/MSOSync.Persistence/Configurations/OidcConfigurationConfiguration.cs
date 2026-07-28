using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

internal sealed class OidcConfigurationConfiguration : IEntityTypeConfiguration<OidcConfiguration>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<OidcConfiguration> builder)
    {
        builder.ToTable("oidc_configuration", Schema);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Authority).HasColumnName("authority").HasMaxLength(500).IsRequired();
        builder.Property(e => e.ClientId).HasColumnName("client_id").HasMaxLength(200).IsRequired();
        builder.Property(e => e.ClientSecretKey).HasColumnName("client_secret_key").HasMaxLength(500).IsRequired();
        builder.Property(e => e.Scopes).HasColumnName("scopes").HasMaxLength(500);
        builder.Property(e => e.CallbackPath).HasColumnName("callback_path").HasMaxLength(200);
        builder.Property(e => e.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(7)");
    }
}
