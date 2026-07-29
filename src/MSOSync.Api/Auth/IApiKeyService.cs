using MSOSync.Persistence.Entities;

namespace MSOSync.Api.Auth;

public interface IApiKeyService
{
    Task<(string RawKey, SyncUserApiKey Entity)> CreateUserKeyAsync(
        long userId, string name, DateTime? expiresAt = null, CancellationToken ct = default);

    Task<(string RawKey, SyncServiceAccount Entity)> CreateServiceAccountAsync(
        string name, string[] permissions, CancellationToken ct = default);

    Task<SyncUser?> ValidateUserKeyAsync(string apiKey, CancellationToken ct = default);

    Task<SyncServiceAccount?> ValidateServiceAccountKeyAsync(string apiKey, CancellationToken ct = default);

    Task RevokeUserKeyAsync(int keyId, CancellationToken ct = default);

    Task RevokeServiceAccountAsync(int id, CancellationToken ct = default);
}
