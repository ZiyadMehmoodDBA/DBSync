namespace MSOSync.Persistence.Entities;

public sealed class OidcConfiguration
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretKey { get; set; } = string.Empty;
    public string Scopes { get; set; } = "openid profile email";
    public string CallbackPath { get; set; } = "/auth/oidc/callback";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
