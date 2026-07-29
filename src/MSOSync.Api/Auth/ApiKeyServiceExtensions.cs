using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Api.Auth;

public static class ApiKeyServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IApiKeyService"/> (API key + service account management)
    /// with the DI container. Call from Program.cs / startup.
    /// </summary>
    public static IServiceCollection AddApiKeyService(this IServiceCollection services)
    {
        services.AddScoped<IApiKeyService, ApiKeyService>();
        return services;
    }
}
