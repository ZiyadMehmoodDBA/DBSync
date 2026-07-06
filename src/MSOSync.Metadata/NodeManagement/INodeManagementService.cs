using MSOSync.Common.Pagination;

namespace MSOSync.Metadata.NodeManagement;

public interface INodeManagementService
{
    Task<CursorPageResult<RegistrationSummaryDto>> GetRegistrationsAsync(
        RegistrationFilter filter, CancellationToken ct = default);

    Task<RegistrationDetailDto?> GetRegistrationByIdAsync(
        long id, CancellationToken ct = default);

    Task<NodeManagementOverviewDto> GetOverviewAsync(CancellationToken ct = default);
}
