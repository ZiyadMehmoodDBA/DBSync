namespace MSOSync.Metadata.NodeManagement;

public interface IProvisionPackageService
{
    Task StreamPackageAsync(string nodeId, string token, Stream destination, CancellationToken ct = default);
}
