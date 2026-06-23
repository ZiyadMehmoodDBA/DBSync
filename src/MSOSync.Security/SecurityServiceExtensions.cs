using System.Text;
using System.Threading.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MSOSync.Security.Middleware;

namespace MSOSync.Security;

public static class SecurityServiceExtensions
{
    public static IServiceCollection AddSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSecret = configuration["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("MSOSYNC_JWT_SECRET")
            ?? throw new InvalidOperationException(
                "MSOSYNC_JWT_SECRET is required (set via env var or Jwt:Secret config key)");

        if (jwtSecret.Length < 32)
            throw new InvalidOperationException(
                "MSOSYNC_JWT_SECRET must be at least 32 characters");

        var jwtIssuer   = configuration["Jwt:Issuer"]   ?? "msosync";
        var jwtAudience = configuration["Jwt:Audience"] ?? "msosync-dashboard";

        services.AddSingleton<JwtService>();
        services.AddSingleton<BCryptPasswordHasher>();
        services.AddSingleton<PasswordPolicy>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<NodeSecurityService>();
        services.AddScoped<AuditService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<AuditService>());

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateIssuer   = true,
                    ValidIssuer      = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience    = jwtAudience,
                    ClockSkew        = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly",         p => p.RequireRole("ADMIN"));
            options.AddPolicy("OperatorOrAbove",   p => p.RequireRole("ADMIN", "OPERATOR"));
            options.AddPolicy("ViewerOrAbove",     p => p.RequireRole("ADMIN", "OPERATOR", "VIEWER"));
        });

        var loginLimit   = configuration.GetValue<int>("RateLimit:LoginPermitLimit",   10);
        var refreshLimit = configuration.GetValue<int>("RateLimit:RefreshPermitLimit", 30);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy("LoginPolicy", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit       = loginLimit,
                        Window            = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6
                    }));

            options.AddPolicy("RefreshPolicy", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit       = refreshLimit,
                        Window            = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6
                    }));

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }

    public static IApplicationBuilder UseNodeTokenAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<NodeTokenAuthMiddleware>();

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
