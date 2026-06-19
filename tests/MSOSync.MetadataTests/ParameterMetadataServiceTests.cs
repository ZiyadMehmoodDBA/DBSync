using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Services;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class ParameterMetadataServiceTests
{
    private static ParameterMetadataService CreateService(
        out AppDbContext db,
        Mock<IMediator>? mediatorMock = null,
        Mock<ICurrentUserService>? userMock = null)
    {
        db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = (mediatorMock ?? new Mock<IMediator>()).Object;
        var user = (userMock ?? new Mock<ICurrentUserService>()).Object;
        return new ParameterMetadataService(db, cache, mediator, user);
    }

    [Fact]
    public async Task UpdateParameterAsync_KnownParameter_WritesHistoryRow()
    {
        var svc = CreateService(out var db);
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
        await db.SaveChangesAsync();

        await svc.UpdateParameterAsync("sync.batch.size", "200");

        var hist = db.ParameterHists.Single();
        hist.ParameterName.Should().Be("sync.batch.size");
        hist.OldValue.Should().Be("100");
        hist.NewValue.Should().Be("200");
    }

    [Fact]
    public async Task UpdateParameterAsync_PublishesParameterChangedEvent()
    {
        var mediatorMock = new Mock<IMediator>();
        var svc = CreateService(out var db, mediatorMock);
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
        await db.SaveChangesAsync();

        await svc.UpdateParameterAsync("sync.batch.size", "200");

        mediatorMock.Verify(m => m.Publish(
            It.Is<ParameterChangedEvent>(e => e.ParameterName == "sync.batch.size"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateParameterAsync_AfterUpdate_CacheReturnsNewValue()
    {
        var svc = CreateService(out var db);
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
        await db.SaveChangesAsync();

        var before = await svc.GetParameterAsync("sync.batch.size");
        before!.Value.Should().Be("100");

        await svc.UpdateParameterAsync("sync.batch.size", "999");

        var after = await svc.GetParameterAsync("sync.batch.size");
        after!.Value.Should().Be("999");
    }

    [Fact]
    public async Task UpdateParameterAsync_UnknownName_ThrowsNotFoundException()
    {
        var svc = CreateService(out _);

        var act = () => svc.UpdateParameterAsync("unknown.param", "value");

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*unknown.param*");
    }

    [Fact]
    public async Task GetAllParameterHistoryAsync_ReturnsAllRows()
    {
        var svc = CreateService(out var db);
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.batch.size", ParameterValue = "100" });
        db.Parameters.Add(new SyncParameter { ParameterName = "sync.max.retries", ParameterValue = "3" });
        await db.SaveChangesAsync();

        await svc.UpdateParameterAsync("sync.batch.size", "200");
        await svc.UpdateParameterAsync("sync.max.retries", "5");

        var all = await svc.GetAllParameterHistoryAsync();
        all.Should().HaveCount(2);
    }
}
