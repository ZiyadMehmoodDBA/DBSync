namespace MSOSync.Api.Auth;

public sealed class OidcAuthOptions
{
    public const string Section = "Oidc";

    public bool Enabled { get; set; } = false;
    public string ProviderName { get; set; } = "oidc";
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretKey { get; set; } = "Oidc:ClientSecret";
    public string Scopes { get; set; } = "openid profile email";
    public string FrontendCallbackUrl { get; set; } = "/auth/sso-callback";
}
