namespace MSOSync.Secrets;

public sealed class SecretsOptions
{
    public const string Section = "Secrets";

    public string Provider { get; set; } = "Environment";

    public AzureKeyVaultOptions AzureKeyVault { get; set; } = new();
}

public sealed class AzureKeyVaultOptions
{
    public string VaultUri { get; set; } = string.Empty;
    public int CacheTtlSeconds { get; set; } = 300;
}
