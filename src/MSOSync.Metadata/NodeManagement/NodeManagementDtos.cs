using MSOSync.Common.Pagination;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public sealed record RegistrationFilter(
    RegistrationStatus? Status = null,
    RegistrationType?   RegistrationType = null,
    int                 PageSize = 50,
    string?             Cursor = null,
    bool                IncludeTotalCount = false
);

public sealed record RegistrationSummaryDto(
    long               Id,
    string             NodeExternalId,
    string             NodeName,
    RegistrationType   RegistrationType,
    RegistrationStatus Status,
    DateTime           ReceivedAt,
    DateTime?          ProcessedAt,
    string?            ProcessedBy
);

public sealed record RegistrationDetailDto(
    long                     Id,
    string                   NodeExternalId,
    string                   NodeName,
    RegistrationType         RegistrationType,
    RegistrationStatus       Status,
    DateTime                 ReceivedAt,
    DateTime?                ProcessedAt,
    string?                  ProcessedBy,
    RegistrationMetadataDto? Metadata,
    RegistrationDiffDto?     Diff
);

public sealed record NodeManagementOverviewDto(
    int       PendingRegistrations,
    int       PendingRecoveries,
    int       TotalNodes,
    int       ActiveNodes,
    int       OfflineNodes,
    int       DegradedNodes,
    int       TotalGroups,
    DateTime? LastRegistrationAt,
    DateTime? LastApprovalAt,
    DateTime  GeneratedAt
);

public sealed record InboundRegistrationDto(
    string                   ExternalId,
    string                   NodeName,
    string                   NodeType,
    RegistrationMetadataDto? Metadata
);

public sealed record ProvisionRequestDto(
    string  NodeName,
    string  ExternalId,
    string  NodeType,
    string  DbServer,
    string  DbName,
    string? GroupId,
    string? Description
);

public sealed record ProvisionResultDto(string NodeId, string Token);

public sealed record BulkResultItemDto(long Id, string Status);

public sealed record ApproveRegistrationRequest(string? Notes);
public sealed record RejectRegistrationRequest(string? Reason);
public sealed record BulkApproveRequest(IReadOnlyList<long> Ids);
public sealed record BulkRejectRequest(IReadOnlyList<long> Ids, string? Reason);
public sealed record ProvisionPackageRequest(string NodeId, string Token);
