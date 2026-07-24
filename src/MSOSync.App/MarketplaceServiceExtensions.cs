using FluentValidation;
using Microsoft.Extensions.Options;
using MSOSync.Api.Dtos.Marketplace;
using MSOSync.Api.Validators;
using MSOSync.Metadata.Marketplace;
using MSOSync.Persistence.Stores;
using MSOSync.Plugin.Marketplace;

namespace MSOSync.App;

public static class MarketplaceServiceExtensions
{
    public static IServiceCollection AddMarketplace(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<MarketplaceOptions>(
            configuration.GetSection(MarketplaceOptions.SectionName));

        // FluentValidation validators
        services.AddScoped<IValidator<MarketplaceSearchParams>,   MarketplaceSearchParamsValidator>();
        services.AddScoped<IValidator<MarketplaceInstallRequest>, MarketplaceInstallRequestValidator>();
        services.AddScoped<IValidator<BulkUpdateCheckRequest>,    BulkUpdateCheckRequestValidator>();

        // Cache store (Scoped — shares the request-scoped DbContext)
        services.AddScoped<IMarketplaceCacheStore, MarketplaceCacheStore>();

        // Services (Scoped)
        services.AddScoped<IMarketplaceService,   MarketplaceService>();
        services.AddScoped<IPluginUpdateService,  PluginUpdateService>();

        // Named HTTP client for the marketplace registry with standard resilience (Polly v8)
        services.AddHttpClient("MarketplaceRegistry", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<MarketplaceOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.RegistryUrl))
                client.BaseAddress = new Uri(opts.RegistryUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(opts.HttpTimeoutSeconds);
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add(
                "User-Agent",
                $"MSOSync/{typeof(MarketplaceServiceExtensions).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}");
        })
        .AddStandardResilienceHandler();

        return services;
    }
}
