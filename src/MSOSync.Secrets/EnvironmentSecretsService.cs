using Microsoft.Extensions.Configuration;

namespace MSOSync.Secrets;

internal sealed class EnvironmentSecretsService : ISecretsService
{
    private readonly IConfiguration _config;
    private readonly bool _isProduction;

    public EnvironmentSecretsService(IConfiguration config, bool isProduction)
    {
        _config = config;
        _isProduction = isProduction;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        // Try env var first: replace : with __ (double underscore)
        var envKey = key.Replace(":", "__").ToUpperInvariant();
        var value = Environment.GetEnvironmentVariable(envKey);

        // Also try the legacy MSOSYNC_ prefix form for backward compat
        if (value is null)
            value = Environment.GetEnvironmentVariable("MSOSYNC_" + envKey);

        // In non-production environments, fall back to IConfiguration
        if (value is null && !_isProduction)
            value = _config[key];

        return Task.FromResult(value);
    }

    public async Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct);
        return value is null ? null : System.Text.Encoding.UTF8.GetBytes(value);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await GetSecretAsync(key, ct) is not null;
}
