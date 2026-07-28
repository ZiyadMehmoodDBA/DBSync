namespace MSOSync.Secrets;

public interface ISecretsService
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
