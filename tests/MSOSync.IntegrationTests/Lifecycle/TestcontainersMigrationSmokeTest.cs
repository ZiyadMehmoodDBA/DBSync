using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

[Collection("Testcontainers-Lifecycle")]
[Trait("Category", "Testcontainers")]
public sealed class TestcontainersMigrationSmokeTest(TestcontainersLifecycleFixture fx)
{
    [Fact]
    public async Task MigrateAsync_FromEmpty_AllTablesExist()
    {
        var opts = AppDbContext.CreateOptionsBuilder(fx.ConnectionString).Options;

        await using var db = new AppDbContext(opts);

        // Verify key tables exist after migration
        var canConnect = await db.Database.CanConnectAsync();
        canConnect.Should().BeTrue();

        var nodeCount = await db.Nodes.CountAsync();
        nodeCount.Should().Be(0, "fresh database has no nodes");

        // Verify all 48 msosync tables were created
        var tableCount = await db.Database
            .SqlQuery<int>($"SELECT COUNT(1) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'msosync'")
            .SingleAsync();
        tableCount.Should().Be(48, "all migrations should create 48 tables in msosync schema");
    }
}

