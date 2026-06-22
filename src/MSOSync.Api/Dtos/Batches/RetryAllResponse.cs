namespace MSOSync.Api.Dtos.Batches;

public sealed record RetryAllResponse(int Count, DateTime Timestamp, string RequestedBy);
