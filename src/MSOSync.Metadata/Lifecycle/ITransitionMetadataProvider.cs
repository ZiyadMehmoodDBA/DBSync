using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public interface ITransitionMetadataProvider
{
    TransitionsDto GetTransitions(SyncNode node);
}
