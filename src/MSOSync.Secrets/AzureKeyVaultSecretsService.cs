using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;

namespace MSOSync.Secrets;

internal sealed class AzureKeyVaultSecretsService : ISecretsService
{
    private readonly SecretClient _client;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheTtl;

    public AzureKeyVaultSecretsService(
        SecretClient client,
        IMemoryCache cache,
        AzureKeyVaultOptions options)
    {
        _client = client;
        _cache = cache;
        _cacheTtl = TimeSpan.FromSeconds(options.CacheTtlSeconds);
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var vaultKey = MapKey(key);
        if (_cache.TryGetValue<string?>(vaultKey, out var cached)) return cached;

        try
        {
            var response = await _client.GetSecretAsync(vaultKey, version: null, ct);
            var value = response.Value.Value;
            _cache.Set(vaultKey, value, _cacheTtl);
            return value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _cache.Set<string?>(vaultKey, null, TimeSpan.FromSeconds(30)); // brief negative cache
            return null;
        }
    }

    public async Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct);
        return value is null ? null : System.Text.Encoding.UTF8.GetBytes(value);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await GetSecretAsync(key, ct) is not null;

    private static string MapKey(string key)
        => key.Replace(":", "--").Replace(".", "-");
}
