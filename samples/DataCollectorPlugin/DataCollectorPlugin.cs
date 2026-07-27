using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace DataCollectorPlugin;

public sealed class DataCollectorPlugin : PluginBase
{
    private Timer? _pollingTimer;
    private readonly ConcurrentQueue<MetricSample> _metrics = new();

    public override async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);

        // Validate configuration at init time
        var connStr = Context.Configuration.GetValue<string>("ConnectionString", "");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            Context.Logger.LogWarning("No ConnectionString in configuration; polling will not run");
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("DataCollectorPlugin.Start");

        var intervalSeconds = Context.Configuration
            .GetSection("Polling")
            .GetValue("IntervalSeconds", 30);

        Context.Logger.LogInformation(
            "Data collector starting (PluginId: {PluginId}, PollInterval: {Interval}s)",
            Context.Metadata.PluginId,
            intervalSeconds);

        _pollingTimer = new Timer(
            _ => PollDatabase(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(intervalSeconds));

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("DataCollectorPlugin.Stop");
        Context.Logger.LogInformation("Data collector stopping");
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        _pollingTimer?.Dispose();
        await base.DisposeAsync();
    }

    private void PollDatabase()
    {
        try
        {
            var connStr = Context.Configuration.GetValue<string>("ConnectionString", "");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return;
            }

            var tableName = Context.Configuration
                .GetSection("Polling")
                .GetValue("TableName", "dbo.SyncEvents");

            using var conn = new SqlConnection(connStr);
            conn.Open();

            var query = $"SELECT COUNT(*) FROM {tableName}";
            using var cmd = new SqlCommand(query, conn);
            cmd.CommandTimeout = 10;

            var count = (int?)cmd.ExecuteScalar() ?? 0;

            var sample = new MetricSample(tableName, count, DateTime.UtcNow);
            _metrics.Enqueue(sample);

            Context.Logger.LogDebug(
                "Collected metric: {TableName} has {RowCount} rows",
                tableName,
                count);

            if (count > 10000)
            {
                Context.Logger.LogWarning(
                    "High row count detected: {TableName} = {RowCount}",
                    tableName,
                    count);
            }
        }
        catch (SqlException ex)
        {
            Context.Logger.LogError(ex, "SQL error during polling");
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "Unexpected error during polling");
        }
    }
}
