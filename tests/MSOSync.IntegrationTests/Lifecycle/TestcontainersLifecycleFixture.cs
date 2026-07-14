using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

public sealed class TestcontainersLifecycleFixture : IAsyncLifetime
{
    // Build is deferred to InitializeAsync so that fixture construction never
    // throws when Docker is unavailable â€” the test itself will fail (or be
    // filtered via --filter "Category!=Testcontainers") instead of crashing.
    private MsSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Container not started.");

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _container.StartAsync();

        var opts = AppDbContext.CreateOptionsBuilder(ConnectionString).Options;

        await using var db = new AppDbContext(opts);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition("Testcontainers-Lifecycle")]
public sealed class TestcontainersLifecycleCollection
    : ICollectionFixture<TestcontainersLifecycleFixture> { }

