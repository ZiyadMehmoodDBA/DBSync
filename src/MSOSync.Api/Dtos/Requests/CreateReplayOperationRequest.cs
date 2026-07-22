namespace MSOSync.Api.Dtos.Requests;

public sealed record CreateReplayOperationRequest(
    string    NodeId,
    string    ReplayMode,   // "FailedDelivery" | "MissedData" | "Both"
    DateTime  FromTime,
    DateTime  ToTime,
    string[]? ChannelIds,
    long[]?   BatchIds);
