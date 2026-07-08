using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MSOSync.Persistence;

namespace MSOSync.ConfigurationTests;

/// SQLite-compatible AppDbContext: rowversion columns mapped to BLOB with default.
/// Mirrors the pattern in MSOSync.MetadataTests.TestDbContext.
public sealed class ConfigurationTestDbContext : AppDbContext
{
    public ConfigurationTestDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SQLite does not support SQL Server-specific column types or rowversion.
        // Clear explicit column types and neutralize rowversion / DateTimeOffset behavior.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                prop.SetColumnType(null);
                if (prop.ClrType == typeof(byte[]) && prop.IsConcurrencyToken)
                {
                    prop.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                    prop.IsNullable = true;
                }
                if (prop.ClrType == typeof(DateTimeOffset) || prop.ClrType == typeof(DateTimeOffset?))
                {
                    prop.SetValueConverter(new DateTimeOffsetToBinaryConverter());
                }
            }
        }
    }
}
