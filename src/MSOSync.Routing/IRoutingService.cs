namespace MSOSync.Routing;

public interface IRoutingService
{
    Task<IReadOnlyList<string>> ResolveAsync(string triggerId, CancellationToken ct = default);
}
