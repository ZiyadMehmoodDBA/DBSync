using MSOSync.Metadata.Operations.Cluster.Diagnostics;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using FluentAssertions;
using Xunit;

namespace MSOSync.MetadataTests.Operations;

public sealed class ClusterDiagnosticsQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ClusterDiagnosticsQueryService _svc;

    public ClusterDiagnosticsQueryServiceTests()
    {
        _db  = TestDbContext.Create();
        _svc = new ClusterDiagnosticsQueryService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetDiagnosticsAsync_EmptyDb_ReturnsEmptyListsWithoutError()
    {
        var result = await _svc.GetDiagnosticsAsync(default);

        result.RuntimeStats.Should().BeEmpty();
        result.ActiveLocks.Should().BeEmpty();
        result.SlowOperations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_RuntimeStats_ConvertsBytesToMb()
    {
        var tenantId = Guid.NewGuid();
        _db.Set<SyncRuntimeStats>().Add(new SyncRuntimeStats
        {
            StatId      = 1,
            HeapUsed    = 104_857_600L, // 100 MB
            HeapMax     = 524_288_000L, // 500 MB
            CpuPercent  = 25.5m,
            ThreadCount = 40,
            GcCount     = 1234L,
            UptimeMs    = 7_200_000L,   // 2 hours
            CreateTime  = DateTime.UtcNow,
            TenantId    = tenantId,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.RuntimeStats.Should().HaveCount(1);
        var s = result.RuntimeStats[0];
        s.HeapUsedMb.Should().BeApproximately(100.0, 0.01);
        s.HeapMaxMb.Should().BeApproximately(500.0, 0.01);
        s.CpuPercent.Should().BeApproximately(25.5, 0.01);
        s.UptimeHours.Should().BeApproximately(2.0, 0.01);
        s.ThreadCount.Should().Be(40);
        s.GcCount.Should().Be(1234L);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_RuntimeStats_LimitedTo50MostRecent()
    {
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 60; i++)
        {
            _db.Set<SyncRuntimeStats>().Add(new SyncRuntimeStats
            {
                StatId     = i + 1,
                CreateTime = DateTime.UtcNow.AddMinutes(-i),
                TenantId   = tenantId,
            });
        }
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.RuntimeStats.Should().HaveCount(50);
        // Most recent first
        result.RuntimeStats[0].StatId.Should().Be(1);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ActiveLock_IsStale_WhenOlderThan5Minutes()
    {
        _db.Set<SyncLock>().Add(new SyncLock
        {
            LockName  = "sync-lock-1",
            LockOwner = "worker-a",
            LockTime  = DateTime.UtcNow.AddMinutes(-10), // 10 min old = stale
            Scope     = LockScope.Platform,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.ActiveLocks.Should().HaveCount(1);
        result.ActiveLocks[0].LockName.Should().Be("sync-lock-1");
        result.ActiveLocks[0].LockOwner.Should().Be("worker-a");
        result.ActiveLocks[0].AgeSeconds.Should().BeGreaterThan(300);
        result.ActiveLocks[0].IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ActiveLock_IsNotStale_WhenFresh()
    {
        _db.Set<SyncLock>().Add(new SyncLock
        {
            LockName  = "fresh-lock",
            LockOwner = "worker-b",
            LockTime  = DateTime.UtcNow.AddSeconds(-30),
            Scope     = LockScope.Platform,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.ActiveLocks[0].IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_SlowOps_OnlyRunningAndPending()
    {
        var tenantId = Guid.NewGuid();
        _db.Operations.AddRange(
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export",  Status = "Running",   StartedAt = DateTime.UtcNow.AddMinutes(-5), TenantId = tenantId, Source = "User" },
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Rollout", Status = "Pending",   StartedAt = DateTime.UtcNow.AddMinutes(-2), TenantId = tenantId, Source = "User" },
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export",  Status = "Completed", StartedAt = DateTime.UtcNow.AddMinutes(-1), TenantId = tenantId, Source = "User" },
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export",  Status = "Failed",    StartedAt = DateTime.UtcNow.AddMinutes(-1), TenantId = tenantId, Source = "User" });
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.SlowOperations.Should().HaveCount(2);
        result.SlowOperations.Should().AllSatisfy(op =>
            (op.Status == "Running" || op.Status == "Pending").Should().BeTrue());
    }

    [Fact]
    public async Task GetDiagnosticsAsync_SlowOps_LimitedTo20OrderedByStartedAtAsc()
    {
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 25; i++)
        {
            _db.Operations.Add(new SyncOperation
            {
                OperationId   = Guid.NewGuid(),
                OperationType = "Export",
                Status        = "Running",
                StartedAt     = DateTime.UtcNow.AddMinutes(-(25 - i)),
                TenantId      = tenantId,
                Source        = "User",
            });
        }
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.SlowOperations.Should().HaveCount(20);
        // Ordered by StartedAt ASC = oldest first = longest DurationMinutes first (descending)
        result.SlowOperations.Should().BeInDescendingOrder(op => op.DurationMinutes, because: "oldest start = longest running = largest DurationMinutes");
    }
}
