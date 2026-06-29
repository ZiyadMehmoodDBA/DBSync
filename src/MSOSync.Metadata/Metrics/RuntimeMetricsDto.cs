namespace MSOSync.Metadata.Metrics;

public sealed record RuntimeMetricsDto(
    long?    HeapUsed,
    long?    HeapMax,
    int?     ThreadCount,
    decimal? CpuPercent,
    long?    GcCount,
    long?    GcTimeMs,
    long?    UptimeMs,
    DateTime CreateTime);
