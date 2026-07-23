namespace MSOSync.Plugin.Security;

public sealed class PluginSecurityOptions
{
    /// <summary>
    /// When true, all .msopkg packages must carry a valid signature from a trusted publisher.
    /// When false, unsigned packages are accepted (local dev mode).
    /// A present-but-invalid signature always fails regardless of this setting.
    /// Default: false.
    /// </summary>
    public bool RequireSignedPackages { get; set; } = false;

    /// <summary>
    /// When true, signed packages must additionally have their publisher in the trusted registry.
    /// Has no effect when RequireSignedPackages = false and the package has no signature block.
    /// Default: true.
    /// </summary>
    public bool RequireTrustedPublisher { get; set; } = true;

    /// <summary>
    /// Path to the trusted publishers JSON file.
    /// Resolved relative to AppContext.BaseDirectory.
    /// Default: "trusted-publishers.json".
    /// </summary>
    public string TrustedPublishersPath { get; set; } = "trusted-publishers.json";

    /// <summary>
    /// Algorithm for IPluginSigner. Supported values: "RSA-PSS-SHA256".
    /// Default: "RSA-PSS-SHA256".
    /// </summary>
    public string PreferredSigningAlgorithm { get; set; } = "RSA-PSS-SHA256";
}
