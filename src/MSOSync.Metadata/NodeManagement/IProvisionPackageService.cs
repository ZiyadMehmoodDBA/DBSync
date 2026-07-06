namespace MSOSync.Metadata.NodeManagement;

public interface IProvisionPackageService
{
    Task StreamPackageAsync(string nodeId, string token, string actorUsername, Stream destination, CancellationToken ct = default);
}
