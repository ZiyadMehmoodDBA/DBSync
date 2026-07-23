using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[GlobalEntity]
public sealed class SyncPlugin
{
    public string   PluginId      { get; set; } = null!;
    public string   PluginName    { get; set; } = null!;
    public string   PluginVersion { get; set; } = null!;
    public string   Status        { get; set; } = null!;
    public bool     Enabled       { get; set; } = true;
    public DateTime InstalledAt   { get; set; }
    public DateTime LastSeenAt    { get; set; }
    public string?  LastError     { get; set; }
    public string?  ManifestHash       { get; set; }
    public string?  HostVersion        { get; set; }
    // 2C.1 packaging additions:
    public string?  PackageHash        { get; set; }   // SHA-256 of the .msopkg file
    public string?  SignedBy           { get; set; }   // publicKeyId from signature block, null if unsigned
    public string?  SignatureAlgorithm { get; set; }   // "RSA-PSS-SHA256" or null if unsigned
    public bool     IsPackageInstall   { get; set; }   // true = installed via .msopkg, false = directory-based
}
