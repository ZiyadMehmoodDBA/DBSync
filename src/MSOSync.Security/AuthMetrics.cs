using System.Diagnostics.Metrics;

namespace MSOSync.Security;

public sealed class AuthMetrics
{
    private static readonly Meter Meter = new("MSOSync.Security", "1.0.0");

    public Counter<long> LoginAttempts  { get; } = Meter.CreateCounter<long>(
        "msosync_auth_login_attempts_total",  description: "Total login attempts");

    public Counter<long> LoginFailures  { get; } = Meter.CreateCounter<long>(
        "msosync_auth_login_failures_total",  description: "Total login failures");

    public Counter<long> RefreshTotal   { get; } = Meter.CreateCounter<long>(
        "msosync_auth_refresh_total",         description: "Total refresh attempts");

    public Counter<long> RateLimitHits  { get; } = Meter.CreateCounter<long>(
        "msosync_auth_rate_limit_hits_total", description: "Total rate limit rejections");
}
