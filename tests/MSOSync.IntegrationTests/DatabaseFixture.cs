// tests/MSOSync.IntegrationTests/DatabaseFixture.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSync_IntegrationTests;Trusted_Connection=True;TrustServerCertificate=True;";

    public AppDbContext Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        Db = new AppDbContext(opts);
        await Db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Db.Database.EnsureDeletedAsync();
        await Db.DisposeAsync();
    }
}
