using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.ConfigurationTests;

public sealed class ConfigurationDbFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Db { get; }

    public ConfigurationDbFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery))
            .Options;

        Db = new ConfigurationTestDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
