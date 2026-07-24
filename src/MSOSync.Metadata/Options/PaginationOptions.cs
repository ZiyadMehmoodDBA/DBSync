namespace MSOSync.Metadata.Options;

public sealed class PaginationOptions
{
    public const string Section = "Pagination";

    /// <summary>
    /// When the node count reaches this threshold, GET /api/v1/nodes returns a pagination-required
    /// response instead of the full list. Default: 200.
    /// </summary>
    public int NodeListCursorThreshold { get; init; } = 200;
}
