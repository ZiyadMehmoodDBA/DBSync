namespace MSOSync.Api.Dtos.Cluster;

public sealed record GetHealthTrendsRequest(string Window = "6h", string? NodeId = null);
