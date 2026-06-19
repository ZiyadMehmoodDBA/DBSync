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

public sealed class TriggerMetadataServiceTests
{
    private static TriggerMetadataService CreateService(
        out AppDbContext db,
        Mock<IMediator>? mediatorMock = null)
    {
        db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = (mediatorMock ?? new Mock<IMediator>()).Object;
        return new TriggerMetadataService(db, cache, mediator);
    }

    [Fact]
    public async Task CreateTriggerAsync_WritesHistoryRow()
    {
        var svc = CreateService(out var db);
        var req = new CreateTriggerRequest("t-1", "dbo.Orders", "default");

        await svc.CreateTriggerAsync(req);

        db.TriggerHists.Should().ContainSingle(h => h.TriggerId == "t-1" && h.TriggerVersion == 1);
    }

    [Fact]
    public async Task CreateTriggerAsync_DuplicateId_ThrowsDuplicateEntityException()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId = "t-dup", SourceTable = "dbo.T", ChannelId = "ch", TriggerVersion = 1
        });
        await db.SaveChangesAsync();

        var act = () => svc.CreateTriggerAsync(
            new CreateTriggerRequest("t-dup", "dbo.Other", "ch"));

        await act.Should().ThrowAsync<DuplicateEntityException>();
    }

    [Fact]
    public async Task UpdateTriggerAsync_BumpsTriggerVersion()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId = "t-2", SourceTable = "dbo.T", ChannelId = "ch", TriggerVersion = 1
        });
        await db.SaveChangesAsync();

        var req = new UpdateTriggerRequest("dbo.T", "ch", true, true, false);
        var result = await svc.UpdateTriggerAsync("t-2", req);

        result.TriggerVersion.Should().Be(2);
        db.TriggerHists.Should().ContainSingle(h => h.TriggerId == "t-2" && h.TriggerVersion == 2);
    }

    [Fact]
    public async Task GetTriggersForChannelAsync_ReturnsOnlyMatchingChannel()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger { TriggerId = "t-a", SourceTable = "A", ChannelId = "ch1", TriggerVersion = 1 });
        db.Triggers.Add(new SyncTrigger { TriggerId = "t-b", SourceTable = "B", ChannelId = "ch2", TriggerVersion = 1 });
        await db.SaveChangesAsync();

        var result = await svc.GetTriggersForChannelAsync("ch1");

        result.Should().ContainSingle(t => t.TriggerId == "t-a");
        result.Should().NotContain(t => t.TriggerId == "t-b");
    }

    [Fact]
    public async Task EnableTriggerAsync_SetsEnabledTrue()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId = "t-3", SourceTable = "dbo.T", ChannelId = "ch", Enabled = false, TriggerVersion = 1
        });
        await db.SaveChangesAsync();

        await svc.EnableTriggerAsync("t-3");

        db.Triggers.Single().Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task DisableTriggerAsync_SetsEnabledFalse()
    {
        var svc = CreateService(out var db);
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId = "t-4", SourceTable = "dbo.T", ChannelId = "ch", Enabled = true, TriggerVersion = 1
        });
        await db.SaveChangesAsync();

        await svc.DisableTriggerAsync("t-4");

        db.Triggers.Single().Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task AddTriggerRouterAsync_CreatesTriggerRouterRow()
    {
        var svc = CreateService(out var db);

        await svc.AddTriggerRouterAsync("t-5", "r-1");

        db.TriggerRouters.Should().ContainSingle(tr => tr.TriggerId == "t-5" && tr.RouterId == "r-1");
    }

    [Fact]
    public async Task RemoveTriggerRouterAsync_DeletesTriggerRouterRow()
    {
        var svc = CreateService(out var db);
        db.TriggerRouters.Add(new SyncTriggerRouter { TriggerId = "t-6", RouterId = "r-2", Enabled = true });
        await db.SaveChangesAsync();

        await svc.RemoveTriggerRouterAsync("t-6", "r-2");

        db.TriggerRouters.Should().BeEmpty();
    }
}
