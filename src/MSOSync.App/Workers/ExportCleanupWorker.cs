using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Metadata.Export;

namespace MSOSync.App.Workers;

public sealed class ExportCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ExportCleanupWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IExportJobService>();
                await svc.ExpireJobsAsync(stoppingToken);
                logger.LogDebug("Export cleanup completed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ExportCleanupWorker");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
