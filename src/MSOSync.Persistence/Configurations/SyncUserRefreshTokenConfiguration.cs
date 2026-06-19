using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserRefreshTokenConfiguration : IEntityTypeConfiguration<SyncUserRefreshToken>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUserRefreshToken> builder)
    {
        builder.ToTable("sync_user_refresh_token", Schema);
        builder.HasKey(e => e.TokenId);

        builder.Property(e => e.TokenId).HasColumnName("token_id").ValueGeneratedOnAdd();
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.TokenHash).HasColumnName("token_hash").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false).IsRequired();
        builder.Property(e => e.IssuedAt).HasColumnName("issued_at").HasColumnType("datetime2(7)").IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime2(7)").IsRequired();
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime2(7)");
        builder.Property(e => e.FamilyId).HasColumnName("family_id");

        builder.HasOne<SyncUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasConstraintName("FK_sync_user_refresh_token_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TokenHash).HasDatabaseName("IX_sync_user_refresh_token_hash");
        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_sync_user_refresh_token_user_id");
    }
}
