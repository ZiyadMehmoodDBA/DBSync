using MediatR;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Caching;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Services;

public sealed class ChannelMetadataService(
    AppDbContext db,
    ICacheService cache,
    IMediator mediator) : IChannelMetadataService
{

    public async Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(CancellationToken ct = default)
    {
        var channels = await db.Channels.AsNoTracking().ToListAsync(ct);
        return channels.Select(MapChannel).ToList().AsReadOnly();
    }

    public async Task<ChannelDto?> GetChannelAsync(string channelId, CancellationToken ct = default)
    {
        var cached = await cache.GetAsync<ChannelDto>(CacheKeyHelper.Channel(channelId), ct);
        if (cached is not null) return cached;

        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChannelId == channelId, ct);
        if (channel == null) return null;

        var dto = MapChannel(channel);
        await cache.SetAsync(CacheKeyHelper.Channel(channelId), dto, TimeSpan.FromSeconds(60), ct);
        return dto;
    }

    public async Task<ChannelDto> CreateChannelAsync(CreateChannelRequest req, CancellationToken ct = default)
    {
        if (await db.Channels.AnyAsync(c => c.ChannelId == req.ChannelId, ct))
            throw new DuplicateEntityException($"Channel '{req.ChannelId}' already exists", "DUPLICATE_CHANNEL");

        var channel = new SyncChannel
        {
            ChannelId = req.ChannelId,
            Priority = req.Priority,
            BatchSize = req.BatchSize,
            MaxBatchToSend = req.MaxBatchToSend,
            MaxDataSize = req.MaxDataSize,
            Enabled = true
        };
        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);
        await mediator.Publish(new ChannelMetadataChangedEvent(channel.ChannelId, "CREATED"), ct);
        return MapChannel(channel);
    }

    public async Task<ChannelDto> UpdateChannelAsync(string channelId, UpdateChannelRequest req, CancellationToken ct = default)
    {
        var channel = await db.Channels.FindAsync([channelId], ct)
            ?? throw new NotFoundException($"Channel '{channelId}' not found", "CHANNEL_NOT_FOUND");

        channel.Priority = req.Priority;
        channel.BatchSize = req.BatchSize;
        channel.MaxBatchToSend = req.MaxBatchToSend;
        channel.MaxDataSize = req.MaxDataSize;

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeyHelper.Channel(channelId), ct);
        await mediator.Publish(new ChannelMetadataChangedEvent(channelId, "UPDATED"), ct);
        return MapChannel(channel);
    }

    public async Task DeleteChannelAsync(string channelId, CancellationToken ct = default)
    {
        var channel = await db.Channels.FindAsync([channelId], ct)
            ?? throw new NotFoundException($"Channel '{channelId}' not found", "CHANNEL_NOT_FOUND");

        db.Channels.Remove(channel);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(CacheKeyHelper.Channel(channelId), ct);
        await mediator.Publish(new ChannelMetadataChangedEvent(channelId, "DELETED"), ct);
    }

    private static ChannelDto MapChannel(SyncChannel c) =>
        new(c.ChannelId, c.Priority, c.BatchSize, c.MaxBatchToSend, c.MaxDataSize, c.Enabled);
}
