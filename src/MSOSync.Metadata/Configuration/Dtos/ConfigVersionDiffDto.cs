namespace MSOSync.Metadata.Configuration.Dtos;

public sealed record ConfigVersionDiffDto(
    Guid                        TemplateId,
    int                         V1,
    int                         V2,
    string                      V1Label,
    string                      V2Label,
    IReadOnlyList<DiffEntryDto> Entries,
    bool                        HasChanges);

public sealed record DiffEntryDto(
    string  Key,
    string  ChangeType,   // "Added" | "Removed" | "Changed" | "Unchanged"
    string? OldValue,
    string? NewValue);
