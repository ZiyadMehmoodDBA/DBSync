using Microsoft.Extensions.Logging;

namespace MSOSync.Plugin.Marketplace;

/// <summary>Structured log event IDs for the marketplace subsystem.</summary>
public static class MarketplaceLogEvents
{
    public static readonly EventId SearchFetched       = new(2001, "Marketplace2001");
    public static readonly EventId PluginDetailFetched = new(2002, "Marketplace2002");
    public static readonly EventId SearchFailed        = new(2003, "Marketplace2003");
    public static readonly EventId PluginFetchFailed   = new(2004, "Marketplace2004");
    public static readonly EventId CacheWritten        = new(2005, "Marketplace2005");
    public static readonly EventId CacheMiss           = new(2006, "Marketplace2006");
    public static readonly EventId InstallTriggered    = new(2007, "Marketplace2007");
    public static readonly EventId BulkUpdateChecked   = new(2008, "Marketplace2008");
    public static readonly EventId ExpiredPurged       = new(2009, "Marketplace2009");
}
