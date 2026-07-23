namespace MSOSync.Plugin.Models;

public sealed class PluginRecord
{
    public string   PluginId      { get; set; } = null!;
    public string   PluginName    { get; set; } = null!;
    public string   PluginVersion { get; set; } = null!;
    public string   Status        { get; set; } = null!;   // PluginStatus enum name
    public bool     Enabled       { get; set; } = true;
    public DateTime InstalledAt   { get; set; }
    public DateTime LastSeenAt    { get; set; }
    public string?  LastError     { get; set; }
    public string?  ManifestHash       { get; set; }
    public string?  HostVersion        { get; set; }
    // 2C.1 packaging additions:
    public string?  PackageHash        { get; set; }
    public string?  SignedBy           { get; set; }
    public string?  SignatureAlgorithm { get; set; }
    public bool     IsPackageInstall   { get; set; }
}
