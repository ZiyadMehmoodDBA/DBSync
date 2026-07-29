using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Api.Auth;

public static class ApiKeyServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IApiKeyService"/> (API key + service account management)
    /// and the <see cref="ApiKeyAuthenticationHandler"/> scheme with the DI container.
    /// Call from Program.cs / startup.
    /// </summary>
    public static IServiceCollection AddApiKeyService(this IServiceCollection services)
    {
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { });
        return services;
    }
}
