namespace MSOSync.Metadata.NodeManagement;

public sealed record RegistrationDiffDto(
    IReadOnlyList<RegistrationDiffItemDto> Items
);

public sealed record RegistrationDiffItemDto(
    string                 Field,
    string?                CurrentValue,
    string?                IncomingValue,
    RegistrationChangeType ChangeType
);

public enum RegistrationChangeType { Unchanged, Added, Modified, Removed }
