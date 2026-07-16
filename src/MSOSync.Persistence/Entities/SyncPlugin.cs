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
    public string?  ManifestHash  { get; set; }
    public string?  HostVersion   { get; set; }
}
