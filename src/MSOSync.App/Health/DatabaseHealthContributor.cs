using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Health;
using MSOSync.Persistence;

namespace MSOSync.App.Health;

public sealed class DatabaseHealthContributor(IServiceScopeFactory scopeFactory)
    : ISystemHealthContributor
{
    public string Name => "Database";

    public async Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var canConnect = await db.Database.CanConnectAsync(cts.Token);
            sw.Stop();

            if (!canConnect)
                return new HealthContribution(Name, "Unhealthy", "Database connection refused", null);

            return new HealthContribution(
                Name, "Healthy",
                $"Database reachable ({sw.ElapsedMilliseconds} ms)",
                null);
        }
        catch (OperationCanceledException)
        {
            return new HealthContribution(Name, "Unhealthy", "Database connection timed out (>3 s)", null);
        }
        catch (Exception ex)
        {
            return new HealthContribution(Name, "Unhealthy", "Database connection failed", ex.Message);
        }
    }
}
