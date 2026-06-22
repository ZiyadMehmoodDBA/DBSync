using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.TransportTests;

internal sealed class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // SQLite doesn't support SQL Server column types — clear explicit types
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                prop.SetColumnType(null);
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
