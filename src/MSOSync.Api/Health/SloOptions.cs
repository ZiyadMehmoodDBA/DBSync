namespace MSOSync.Api.Health;

public sealed class SloOptions
{
    public const string Section = "Slo";

    public double DeliveryRateTarget { get; set; } = 0.999;
    public double LatencyP99TargetMs { get; set; } = 5000;
    public int WindowHours { get; set; } = 24;
}
