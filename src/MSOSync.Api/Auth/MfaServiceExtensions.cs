using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Api.Auth;

public static class MfaServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IMfaService"/> (TOTP-based) with the DI container.
    /// Call from Program.cs / startup.
    /// </summary>
    public static IServiceCollection AddMfaService(this IServiceCollection services)
    {
        services.AddScoped<IMfaService, TotpMfaService>();
        return services;
    }
}
