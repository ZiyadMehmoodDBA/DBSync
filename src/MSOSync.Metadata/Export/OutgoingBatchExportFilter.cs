namespace MSOSync.Metadata.Export;

public sealed class OutgoingBatchExportFilter
{
    public string? NodeId    { get; set; }
    public string? ChannelId { get; set; }
    public string? Status    { get; set; }  // parsed to BatchStatus enum in service
}
