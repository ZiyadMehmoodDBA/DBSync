using FluentAssertions;
using MSOSync.Metadata.Locks;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Audit;

public sealed class LockAdminServiceTests : IDisposable
{
    private readonly AppDbContext     _db;
    private readonly LockAdminService _sut;

    public LockAdminServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new LockAdminService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetLocks_ReturnsAllOrderedByName()
    {
        _db.Locks.AddRange(
            new SyncLock { LockName = "SYNC_ENGINE",  LockOwner = "worker-1", LockTime = DateTime.UtcNow },
            new SyncLock { LockName = "RETRY_ENGINE", LockOwner = "worker-2", LockTime = DateTime.UtcNow },
            new SyncLock { LockName = "PURGE_ENGINE", LockOwner = null,        LockTime = null });
        await _db.SaveChangesAsync();

        var result = await _sut.GetLocksAsync(default);

        result.Should().HaveCount(3);
        result[0].LockName.Should().Be("PURGE_ENGINE");
        result[1].LockName.Should().Be("RETRY_ENGINE");
        result[2].LockName.Should().Be("SYNC_ENGINE");
    }

    [Fact]
    public async Task GetLocks_Empty_ReturnsEmptyList()
    {
        var result = await _sut.GetLocksAsync(default);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteLock_Exists_DeletesAndReturnsTrue()
    {
        _db.Locks.Add(new SyncLock { LockName = "SYNC_ENGINE", LockOwner = "worker-1" });
        await _db.SaveChangesAsync();

        var deleted = await _sut.DeleteLockAsync("SYNC_ENGINE", default);

        deleted.Should().BeTrue();
        (await _db.Locks.FindAsync("SYNC_ENGINE")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteLock_NotFound_ReturnsFalse()
    {
        var deleted = await _sut.DeleteLockAsync("NONEXISTENT", default);
        deleted.Should().BeFalse();
    }
}
