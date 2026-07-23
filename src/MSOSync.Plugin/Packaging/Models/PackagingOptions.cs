namespace MSOSync.Plugin.Packaging.Models;

public sealed class PackagingOptions
{
    /// <summary>Maximum uncompressed size of a .msopkg archive in bytes. Default: 50 MB.</summary>
    public long MaxPackageSizeBytes { get; set; } = 52_428_800;

    /// <summary>Maximum number of entries inside the archive. Default: 200.</summary>
    public int MaxFileCount { get; set; } = 200;

    /// <summary>Maximum total uncompressed size of the assets/ directory in bytes. Default: 2 MB.</summary>
    public long MaxAssetsSizeBytes { get; set; } = 2_097_152;

    /// <summary>Maximum number of files inside assets/. Default: 20.</summary>
    public int MaxAssetsFileCount { get; set; } = 20;
}
