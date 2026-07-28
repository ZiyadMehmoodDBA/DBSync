using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MSOSync.Persistence.Entities;
using MSOSync.Secrets;
using MSOSync.Security;

namespace MSOSync.Api.Auth;

public static class OidcAuthenticationExtensions
{
    public static IServiceCollection AddOidcAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IOidcUserProvisioningService, OidcUserProvisioningService>();

        services.AddOptions<OidcAuthOptions>()
            .BindConfiguration(OidcAuthOptions.Section)
            .ValidateOnStart();

        var opts = configuration.GetSection(OidcAuthOptions.Section).Get<OidcAuthOptions>() ?? new();
        if (!opts.Enabled) return services;

        services.AddAuthentication(o =>
        {
            o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, oidcOpts =>
        {
            oidcOpts.Authority = opts.Authority;
            oidcOpts.ClientId = opts.ClientId;
            oidcOpts.ResponseType = "code";
            oidcOpts.SaveTokens = false;
            oidcOpts.GetClaimsFromUserInfoEndpoint = true;
            oidcOpts.CallbackPath = "/auth/oidc/callback";

            foreach (var scope in opts.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                oidcOpts.Scope.Add(scope);

            oidcOpts.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = async ctx =>
                {
                    var secrets = ctx.HttpContext.RequestServices.GetRequiredService<ISecretsService>();
                    ctx.ProtocolMessage.ClientSecret =
                        await secrets.GetSecretAsync(opts.ClientSecretKey) ?? string.Empty;
                },

                OnTokenValidated = async ctx =>
                {
                    var provisioning = ctx.HttpContext.RequestServices
                        .GetRequiredService<IOidcUserProvisioningService>();
                    var user = await provisioning.ProvisionAsync(
                        ctx.Principal!, opts.ProviderName, ctx.HttpContext.RequestAborted);

                    var jwtService = ctx.HttpContext.RequestServices.GetRequiredService<JwtService>();
                    var token = jwtService.CreateAccessToken(user.UserId, user.Username, []);

                    ctx.Response.Redirect(
                        $"{opts.FrontendCallbackUrl}?token={Uri.EscapeDataString(token)}");
                    ctx.HandleResponse();
                }
            };
        });

        return services;
    }
}
