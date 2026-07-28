using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MSOSync.Common.Health;

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

        var opts = config.GetSection(SecretsOptions.Section).Get<SecretsOptions>() ?? new();

        services.AddMemoryCache();
        services.AddSingleton<EnvironmentSecretsService>(sp =>
            new EnvironmentSecretsService(config, isProduction: !env.IsDevelopment()));

        if (opts.Provider == "AzureKeyVault")
        {
            services.AddSingleton<SecretClient>(sp =>
                new SecretClient(new Uri(opts.AzureKeyVault.VaultUri), new DefaultAzureCredential()));
            services.AddSingleton<AzureKeyVaultSecretsService>(sp =>
                new AzureKeyVaultSecretsService(
                    sp.GetRequiredService<SecretClient>(),
                    sp.GetRequiredService<IMemoryCache>(),
                    opts.AzureKeyVault));
            services.AddSingleton<ISystemHealthContributor, KeyVaultHealthContributor>();

            services.AddSingleton<ISecretsService>(sp => new CompositeSecretsService([
                sp.GetRequiredService<AzureKeyVaultSecretsService>(),
                sp.GetRequiredService<EnvironmentSecretsService>()
            ]));
        }
        else
        {
            services.AddSingleton<ISecretsService>(sp => new CompositeSecretsService([
                sp.GetRequiredService<EnvironmentSecretsService>()
            ]));
        }

        return services;
    }

    /// <summary>
    /// Creates a bootstrap secrets reader before the DI container is built.
    /// Use this at startup to read secrets needed during service registration (e.g., JWT signing key).
    /// </summary>
    public static ISecretsService CreateBootstrapSecrets(IConfiguration config, IHostEnvironment env) =>
        new EnvironmentSecretsService(config, isProduction: !env.IsDevelopment());
}
