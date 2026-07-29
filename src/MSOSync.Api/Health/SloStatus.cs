namespace MSOSync.Api.Health;

public sealed record SloStatus(
    double DeliveryRate,
    double DeliveryRateTarget,
    bool DeliveryRateMet,
    double LatencyP99Ms,
    double LatencyP99TargetMs,
    bool LatencyP99Met,
    DateTime WindowStart,
    DateTime WindowEnd);
