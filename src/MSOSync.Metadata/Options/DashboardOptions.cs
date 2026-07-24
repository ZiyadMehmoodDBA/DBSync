namespace MSOSync.Metadata.Options;

public sealed class DashboardOptions
{
    public const string Section = "Dashboard";

    /// <summary>How long to cache the dashboard summary snapshot. Default: 30 seconds.</summary>
    public int SummaryTtlSeconds { get; init; } = 30;
}
