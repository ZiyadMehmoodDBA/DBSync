using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.TransportTests;

internal sealed class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // SQLite doesn't support SQL Server column types or SQL Server-specific
        // default value expressions (e.g. SYSUTCDATETIME()). Clear both.
        // Also mark rowversion properties as nullable with no value generation.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                prop.SetColumnType(null);
                prop.SetDefaultValueSql(null);
                if (prop.ClrType == typeof(byte[]) && prop.IsConcurrencyToken)
                {
                    prop.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                    prop.IsNullable = true;
                }
            }
        }
    }
}

internal static class TestDb
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new TestAppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
