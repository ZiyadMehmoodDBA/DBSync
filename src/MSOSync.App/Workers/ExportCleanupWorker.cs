using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Export;

namespace MSOSync.App.Workers;

public sealed class ExportCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ExportCleanupWorker> logger,
    IWorkerStatusRegistry registry)
    : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.Register(nameof(ExportCleanupWorker), TimeSpan.FromHours(1));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            registry.RecordTickStart(nameof(ExportCleanupWorker));
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IExportJobService>();
                await svc.ExpireJobsAsync(stoppingToken);
                logger.LogDebug("Export cleanup completed");
                registry.RecordTickComplete(nameof(ExportCleanupWorker));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                registry.RecordTickComplete(nameof(ExportCleanupWorker));
                break;
            }
            catch (Exception ex)
            {
                registry.RecordTickFailed(nameof(ExportCleanupWorker), ex);
                logger.LogError(ex, "Error in ExportCleanupWorker");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
