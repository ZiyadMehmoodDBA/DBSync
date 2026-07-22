namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record CreateReplayRequest(
    string    NodeId,
    string    ReplayMode,      // "FailedDelivery" | "MissedData" | "Both"
    DateTime  FromTime,
    DateTime  ToTime,
    string[]? ChannelIds,      // null = all channels
    long[]?   BatchIds,        // null = no cherry-pick; FailedDelivery only
    Guid?     InitiatedBy);
