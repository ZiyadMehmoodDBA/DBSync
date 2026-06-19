using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Services;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class RouterMetadataServiceTests
{
    private static RouterMetadataService CreateService(out AppDbContext db)
    {
        db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = new Mock<IMediator>().Object;
        return new RouterMetadataService(db, cache, mediator);
    }

    [Fact]
    public async Task CreateRouterAsync_DuplicateId_ThrowsDuplicateEntityException()
    {
        var svc = CreateService(out var db);
        db.Routers.Add(new SyncRouter
        {
            RouterId = "r-dup", SourceNodeGroup = "g1", TargetNodeGroup = "g2", RouterType = "default"
        });
        await db.SaveChangesAsync();

        var act = () => svc.CreateRouterAsync(
            new CreateRouterRequest("r-dup", "g1", "g2", "default"));

        await act.Should().ThrowAsync<DuplicateEntityException>();
    }

    [Fact]
    public async Task GetRoutersForSourceGroupAsync_FiltersCorrectly()
    {
        var svc = CreateService(out var db);
        db.Routers.Add(new SyncRouter { RouterId = "r-1", SourceNodeGroup = "src", TargetNodeGroup = "tgt", RouterType = "default" });
        db.Routers.Add(new SyncRouter { RouterId = "r-2", SourceNodeGroup = "other", TargetNodeGroup = "tgt", RouterType = "default" });
        await db.SaveChangesAsync();

        var result = await svc.GetRoutersForSourceGroupAsync("src");

        result.Should().ContainSingle(r => r.RouterId == "r-1");
        result.Should().NotContain(r => r.RouterId == "r-2");
    }

    [Fact]
    public async Task GetRoutersForTargetGroupAsync_FiltersCorrectly()
    {
        var svc = CreateService(out var db);
        db.Routers.Add(new SyncRouter { RouterId = "r-3", SourceNodeGroup = "src", TargetNodeGroup = "tgt-a", RouterType = "default" });
        db.Routers.Add(new SyncRouter { RouterId = "r-4", SourceNodeGroup = "src", TargetNodeGroup = "tgt-b", RouterType = "default" });
        await db.SaveChangesAsync();

        var result = await svc.GetRoutersForTargetGroupAsync("tgt-a");

        result.Should().ContainSingle(r => r.RouterId == "r-3");
    }

    [Fact]
    public async Task DeleteRouterAsync_NonExistent_ThrowsNotFoundException()
    {
        var svc = CreateService(out _);

        var act = () => svc.DeleteRouterAsync("no-such-router");

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
