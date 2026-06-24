using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed class IncomingBatchFilter
{
    public string?              SourceNodeId { get; set; }
    public string?              ChannelId    { get; set; }
    public IncomingBatchStatus? Status       { get; set; }
    public DateTime?            From         { get; set; }
    public DateTime?            To           { get; set; }
    public int                  Page         { get; set; } = 1;
    public int                  PageSize     { get; set; } = 50;
}
