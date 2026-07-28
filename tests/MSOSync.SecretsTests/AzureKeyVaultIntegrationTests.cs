// tests/MSOSync.SecretsTests/AzureKeyVaultIntegrationTests.cs
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Secrets;
using Xunit;

namespace MSOSync.SecretsTests;

[Trait("Category", "Integration")]
public sealed class AzureKeyVaultIntegrationTests
{
    private static readonly string? VaultUri = Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_URI");

    [SkippableFact]
    public async Task GetSecretAsync_ReturnsValue_FromRealVault()
    {
        Skip.If(string.IsNullOrEmpty(VaultUri), "AZURE_KEY_VAULT_URI not set — skipping vault integration test.");

        var client = new SecretClient(new Uri(VaultUri!), new DefaultAzureCredential());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var opts = new AzureKeyVaultOptions { VaultUri = VaultUri!, CacheTtlSeconds = 60 };
        var svc = new AzureKeyVaultSecretsService(client, cache, opts);

        // Requires a secret named "Test--IntegrationKey" in the vault with value "integration-ok"
        var result = await svc.GetSecretAsync("Test:IntegrationKey");

        result.Should().Be("integration-ok");
    }
}
