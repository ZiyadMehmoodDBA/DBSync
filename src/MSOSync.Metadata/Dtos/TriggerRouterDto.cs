namespace MSOSync.Metadata.Dtos;

public sealed record TriggerRouterDto(
    string TriggerId,
    string RouterId,
    bool Enabled);
