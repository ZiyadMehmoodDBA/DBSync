// tests/MSOSync.IntegrationTests/Engine/ApplyEngineFixture.cs
// Uses Testcontainers MsSql when Docker is available; falls back to LocalDB
// when Docker is not present (CI-less local dev environments).
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Engine;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Engine;

public sealed class ApplyEngineFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    /// <summary>Connection string — set in InitializeAsync.</summary>
    public string ConnectionString { get; private set; } = null!;

    public IServiceProvider Services { get; private set; } = null!;

    // ── IAsyncLifetime ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        ConnectionString = await ResolveConnectionStringAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(ConnectionString));
        services.AddSingleton<IClock, FixtureClock>();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddApplyEngine();
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        await CreateTestTableAsync();
        await SeedStaticDataAsync(db);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Try to start a Testcontainers MsSql container.
    /// If Docker is unavailable, fall back to LocalDB with a dedicated test database.
    /// </summary>
    private async Task<string> ResolveConnectionStringAsync()
    {
        try
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();

            await _container.StartAsync();
            return _container.GetConnectionString();
        }
        catch (ArgumentException)
        {
            // Docker not available — use LocalDB
            _container = null;
            return "Server=(localdb)\\mssqllocaldb;Database=MSOSync_ApplyEngineTests;Trusted_Connection=True;TrustServerCertificate=True;";
        }
    }

    private async Task CreateTestTableAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID('dbo.test_orders','U') IS NULL
            CREATE TABLE [dbo].[test_orders] (
                [order_id]  int          NOT NULL,
                [tenant_id] int          NULL,
                [status]    nvarchar(50) NULL,
                CONSTRAINT PK_test_orders PRIMARY KEY ([order_id])
            );
            IF OBJECT_ID('dbo.test_composite','U') IS NULL
            CREATE TABLE [dbo].[test_composite] (
                [tenant_id] int          NOT NULL,
                [order_id]  int          NOT NULL,
                [status]    nvarchar(50) NULL,
                CONSTRAINT PK_test_composite PRIMARY KEY ([tenant_id],[order_id])
            )
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedStaticDataAsync(AppDbContext db)
    {
        // Node group (FK required by SyncNode)
        if (!await db.NodeGroups.AnyAsync(g => g.GroupId == "apply-g"))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = "apply-g", GroupName = "Apply Test Group" });
        await db.SaveChangesAsync();

        // Source node (FK required by SyncIncomingBatch.source_node_id)
        if (!await db.Nodes.AnyAsync(n => n.NodeId == "src"))
            db.Nodes.Add(new SyncNode
            {
                NodeId  = "src",
                GroupId = "apply-g",
                SyncUrl = "http://src",
                Status  = "APPROVED",
            });
        await db.SaveChangesAsync();

        // Channel (FK required by SyncIncomingBatch.channel_id and SyncTrigger.channel_id)
        if (!await db.Channels.AnyAsync(c => c.ChannelId == "default"))
            db.Channels.Add(new SyncChannel
            {
                ChannelId      = "default",
                Priority       = 1,
                BatchSize      = 1000,
                MaxBatchToSend = 100,
                MaxDataSize    = 1048576L,
            });
        await db.SaveChangesAsync();

        // Primary trigger for most tests
        if (!await db.Triggers.AnyAsync(t => t.TriggerId == "t-orders"))
            db.Triggers.Add(new SyncTrigger
            {
                TriggerId      = "t-orders",
                SourceTable    = "dbo.test_orders",
                ChannelId      = "default",
                TriggerVersion = 2,
                PkColumnsJson  = """["order_id"]""",
            });
        await db.SaveChangesAsync();

        // Composite PK trigger for CompositePk tests
        if (!await db.Triggers.AnyAsync(t => t.TriggerId == "trig-composite"))
            db.Triggers.Add(new SyncTrigger
            {
                TriggerId      = "trig-composite",
                SourceTable    = "dbo.test_composite",
                ChannelId      = "default",
                TriggerVersion = 2,
                PkColumnsJson  = """["tenant_id","order_id"]""",
            });
        await db.SaveChangesAsync();
    }

    /// <summary>Truncate test_orders between tests.</summary>
    public async Task ClearTestOrdersAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM [dbo].[test_orders]";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Truncate test_composite between tests.</summary>
    public async Task ClearTestCompositeAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM [dbo].[test_composite]";
        await cmd.ExecuteNonQueryAsync();
    }
}

file sealed class FixtureClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

[CollectionDefinition("ApplyEngine")]
public sealed class ApplyEngineCollection : ICollectionFixture<ApplyEngineFixture> { }
