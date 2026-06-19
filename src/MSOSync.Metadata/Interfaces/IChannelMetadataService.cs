using MSOSync.Metadata.Dtos;

namespace MSOSync.Metadata.Interfaces;

public interface IChannelMetadataService
{
    Task<IReadOnlyList<ChannelDto>> GetChannelsAsync(CancellationToken ct = default);
    Task<ChannelDto?> GetChannelAsync(string channelId, CancellationToken ct = default);
    Task<ChannelDto> CreateChannelAsync(CreateChannelRequest req, CancellationToken ct = default);
    Task<ChannelDto> UpdateChannelAsync(string channelId, UpdateChannelRequest req, CancellationToken ct = default);
    Task DeleteChannelAsync(string channelId, CancellationToken ct = default);
}
