using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Api.Auth;

public static class ApiKeyServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IApiKeyService"/> (API key + service account management)
    /// with the DI container.
    /// The ApiKey authentication scheme is registered in Program.cs by chaining onto the
    /// already-configured authentication services to avoid resetting the default scheme.
    /// </summary>
    public static IServiceCollection AddApiKeyService(this IServiceCollection services)
    {
        services.AddScoped<IApiKeyService, ApiKeyService>();
        return services;
    }
}
