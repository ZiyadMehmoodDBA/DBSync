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

public sealed class ChannelMetadataServiceTests
{
    private static ChannelMetadataService CreateService(out AppDbContext db)
    {
        db = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mediator = new Mock<IMediator>().Object;
        return new ChannelMetadataService(db, cache, mediator);
    }

    [Fact]
    public async Task CreateChannelAsync_DuplicateId_ThrowsDuplicateEntityException()
    {
        var svc = CreateService(out var db);
        db.Channels.Add(new SyncChannel { ChannelId = "ch-dup", Priority = 1, BatchSize = 1000, MaxBatchToSend = 10, MaxDataSize = 1048576L });
        await db.SaveChangesAsync();

        var act = () => svc.CreateChannelAsync(
            new CreateChannelRequest("ch-dup", 1));

        await act.Should().ThrowAsync<DuplicateEntityException>();
    }

    [Fact]
    public async Task CreateChannelAsync_ValidRequest_PersistsWithDefaults()
    {
        var svc = CreateService(out _);

        var result = await svc.CreateChannelAsync(new CreateChannelRequest("ch-1", 5));

        result.ChannelId.Should().Be("ch-1");
        result.Priority.Should().Be(5);
        result.BatchSize.Should().Be(1000);
        result.MaxBatchToSend.Should().Be(10);
        result.MaxDataSize.Should().Be(1048576L);
        result.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateChannelAsync_UpdatesAllFields()
    {
        var svc = CreateService(out var db);
        db.Channels.Add(new SyncChannel { ChannelId = "ch-2", Priority = 1, BatchSize = 1000, MaxBatchToSend = 10, MaxDataSize = 1048576L });
        await db.SaveChangesAsync();

        var result = await svc.UpdateChannelAsync("ch-2", new UpdateChannelRequest(2, 500, 5, 2097152L));

        result.Priority.Should().Be(2);
        result.BatchSize.Should().Be(500);
        result.MaxBatchToSend.Should().Be(5);
        result.MaxDataSize.Should().Be(2097152L);
    }

    [Fact]
    public async Task DeleteChannelAsync_NonExistent_ThrowsNotFoundException()
    {
        var svc = CreateService(out _);

        var act = () => svc.DeleteChannelAsync("no-such-channel");

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
