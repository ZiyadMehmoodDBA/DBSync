using Azure.Security.KeyVault.Secrets;
using MSOSync.Common.Health;

namespace MSOSync.Secrets;

internal sealed class KeyVaultHealthContributor(SecretClient client) : ISystemHealthContributor
{
    public string Name => "AzureKeyVault";

    public async Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(ct).AsPages().WithCancellation(ct))
                break;
            return new HealthContribution(Name, "Healthy", "Azure Key Vault reachable");
        }
        catch (Exception ex)
        {
            return new HealthContribution(Name, "Degraded", "Azure Key Vault unreachable", ex.Message);
        }
    }
}
