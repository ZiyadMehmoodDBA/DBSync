using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public interface IProvisionPackageService
{
    Task StreamPackageAsync(ProvisionResultDto provision, SyncNode node, Stream destination, CancellationToken ct = default);
}
