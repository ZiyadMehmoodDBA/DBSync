using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public interface IRegistrationDiffService
{
    RegistrationDiffDto Compute(
        RegistrationMetadataDto incoming,
        SyncNode                currentNode,
        bool                    includeUnchanged = false);
}
