namespace MSOSync.Persistence.Entities;

/// Single source for all valid feature flag keys. Validation gate rejects any key not here.
public static class FeatureFlagCatalog
{
    public const string EnableBulkApply    = "enableBulkApply";
    public const string EnableCompression  = "enableCompression";
    public const string EnableParallelSync = "enableParallelSync";

    private static readonly HashSet<string> _supported = new(StringComparer.Ordinal)
    {
        EnableBulkApply,
        EnableCompression,
        EnableParallelSync,
    };

    public static bool IsSupportedKey(string key) => _supported.Contains(key);
}
