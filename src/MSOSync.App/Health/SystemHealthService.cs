using MSOSync.Common.Health;

namespace MSOSync.App.Health;

public sealed class SystemHealthService(IEnumerable<ISystemHealthContributor> contributors)
    : ISystemHealthService
{
    public async Task<HealthContribution[]> GetAllAsync(CancellationToken ct)
    {
        var tasks = contributors
            .Select(c => c.GetAsync(ct))
            .ToArray();

        return await Task.WhenAll(tasks);
    }
}
