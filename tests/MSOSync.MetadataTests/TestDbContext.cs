using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.MetadataTests;

internal sealed class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // SQLite doesn't support SQL Server-specific column types like nvarchar(max) or datetime2(7).
        // Clear explicit column types so EF uses SQLite's default affinity rules.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                prop.SetColumnType(null);
    }
}

internal static class TestDbContext
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
