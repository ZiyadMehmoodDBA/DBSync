using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.App.Export;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Export;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Persistence.Entities;


namespace MSOSync.App.Workers;

public sealed class ExportJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ExportOptions> opts,
    ILogger<ExportJobWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(opts.Value.BasePath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in ExportJobWorker loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessNextJobAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IExportJobService>();

        var job = await jobService.ClaimNextPendingJobAsync(ct);
        if (job is null) return;

        logger.LogInformation("Starting export job {JobId} ({ResourceType}/{Format})",
            job.JobId, job.ResourceType, job.Format);

        try
        {
            var outputPath = Path.Combine(opts.Value.BasePath, $"{job.JobId}.{job.Format}");
            var rowCount = await WriteExportFileAsync(scope.ServiceProvider, job, outputPath, ct);
            await jobService.CompleteJobAsync(job.JobId, outputPath, rowCount, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export job {JobId} failed", job.JobId);
            await jobService.FailJobAsync(job.JobId, ex.Message, ct);
        }
    }

    private static async Task<long> WriteExportFileAsync(
        IServiceProvider sp, SyncExportJob job, string outputPath, CancellationToken ct)
    {
        await using var stream = File.Create(outputPath);
        var isJson = job.Format.Equals("json", StringComparison.OrdinalIgnoreCase);

        switch (job.ResourceType.ToLowerInvariant())
        {
            case "events":
            {
                var filter   = JsonSerializer.Deserialize<EventFilter>(job.FiltersJson) ?? new EventFilter();
                var exporter = sp.GetRequiredService<IExportService<EventFilter>>();
                return isJson
                    ? await exporter.ExportJsonAsync(stream, filter, ct)
                    : await exporter.ExportCsvAsync(stream, filter, ct);
            }

            case "incoming-batches":
            {
                var filter   = JsonSerializer.Deserialize<IncomingBatchFilter>(job.FiltersJson) ?? new IncomingBatchFilter();
                var exporter = sp.GetRequiredService<IExportService<IncomingBatchFilter>>();
                return isJson
                    ? await exporter.ExportJsonAsync(stream, filter, ct)
                    : await exporter.ExportCsvAsync(stream, filter, ct);
            }

            case "outgoing-batches":
            {
                var filter   = JsonSerializer.Deserialize<OutgoingBatchExportFilter>(job.FiltersJson) ?? new OutgoingBatchExportFilter();
                var exporter = sp.GetRequiredService<IExportService<OutgoingBatchExportFilter>>();
                return isJson
                    ? await exporter.ExportJsonAsync(stream, filter, ct)
                    : await exporter.ExportCsvAsync(stream, filter, ct);
            }

            case "audit":
            {
                var filter   = JsonSerializer.Deserialize<AuditFilter>(job.FiltersJson) ?? new AuditFilter();
                var exporter = sp.GetRequiredService<IExportService<AuditFilter>>();
                return isJson
                    ? await exporter.ExportJsonAsync(stream, filter, ct)
                    : await exporter.ExportCsvAsync(stream, filter, ct);
            }

            default:
                throw new InvalidOperationException($"Unknown resource type: {job.ResourceType}");
        }
    }
}
