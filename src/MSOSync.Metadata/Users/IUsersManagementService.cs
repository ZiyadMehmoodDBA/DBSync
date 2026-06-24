namespace MSOSync.Metadata.Users;

public interface IUsersManagementService
{
    Task<PagedResult<UserSummaryDto>> GetUsersAsync(
        int page, int pageSize, bool? enabled, string? search,
        CancellationToken ct = default);

    Task<UserDetailDto?> GetUserAsync(long userId, CancellationToken ct = default);

    Task<UserDetailDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);

    Task<UserDetailDto> UpdateUserAsync(
        long userId, UpdateUserRequest request, CancellationToken ct = default);

    Task DeactivateUserAsync(long userId, CancellationToken ct = default);
}
