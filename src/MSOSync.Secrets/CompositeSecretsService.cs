namespace MSOSync.Secrets;

internal sealed class CompositeSecretsService(IEnumerable<ISecretsService> providers) : ISecretsService
{
    private readonly IReadOnlyList<ISecretsService> _providers = providers.ToList();

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            var value = await provider.GetSecretAsync(key, ct);
            if (value is not null) return value;
        }
        return null;
    }

    public async Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            var value = await provider.GetSecretBytesAsync(key, ct);
            if (value is not null) return value;
        }
        return null;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await GetSecretAsync(key, ct) is not null;
}
