using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MSOSync.Secrets;

public static class SecretsServiceExtensions
{
    public static IServiceCollection AddSecretsService(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env)
    {
        services.AddOptions<SecretsOptions>()
            .BindConfiguration(SecretsOptions.Section)
            .Validate(o => o.Provider is "Environment" or "AzureKeyVault",
                "Secrets:Provider must be 'Environment' or 'AzureKeyVault'")
            .ValidateOnStart();

        services.AddSingleton<EnvironmentSecretsService>(sp =>
            new EnvironmentSecretsService(config, isProduction: !env.IsDevelopment()));

        services.AddSingleton<ISecretsService>(sp =>
        {
            // Providers in resolution order: Azure KV (if registered) → env
            // AzureKeyVaultSecretsService is prepended to this chain in 2E.2
            var providers = new List<ISecretsService>
            {
                sp.GetRequiredService<EnvironmentSecretsService>()
            };
            return new CompositeSecretsService(providers);
        });

        return services;
    }

    /// <summary>
    /// Creates a bootstrap secrets reader before the DI container is built.
    /// Use this at startup to read secrets needed during service registration (e.g., JWT signing key).
    /// </summary>
    public static ISecretsService CreateBootstrapSecrets(IConfiguration config, IHostEnvironment env) =>
        new EnvironmentSecretsService(config, isProduction: !env.IsDevelopment());
}
