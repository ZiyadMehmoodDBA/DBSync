namespace MSOSync.Metadata.Audit;

public sealed record AuditSummaryDto(
    int                             TotalActions,
    int                             FailedOperations,
    int                             PermissionChanges,
    IReadOnlyList<DayBucket>        ByDay,
    IReadOnlyList<UserBucket>       ByUser,
    IReadOnlyList<EntityTypeBucket> ByEntityType,
    IReadOnlyList<ParameterBucket>  TopParameters
);

public sealed record DayBucket(DateOnly Date, int Total, int Failed);
public sealed record UserBucket(string Username, int Count);
public sealed record EntityTypeBucket(string EntityType, int Count);
public sealed record ParameterBucket(string ParameterName, int Count);
