namespace DataCollectorPlugin;

internal sealed record MetricSample(
    string TableName,
    int RowCount,
    DateTime CollectedAt);
