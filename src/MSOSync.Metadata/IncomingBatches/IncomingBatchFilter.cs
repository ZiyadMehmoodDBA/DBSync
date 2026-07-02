using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed class IncomingBatchFilter
{
    public string?              SourceNodeId      { get; set; }
    public string?              ChannelId         { get; set; }
    public IncomingBatchStatus? Status            { get; set; }
    public DateTime?            From              { get; set; }
    public DateTime?            To                { get; set; }
    public string?              Cursor            { get; set; }
    public bool                 IncludeTotalCount { get; set; }
    public int                  PageSize          { get; set; } = 50;
}
