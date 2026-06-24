namespace MSOSync.Metadata.Events;

public sealed class EventFilter
{
    public string?   SourceNodeId { get; set; }
    public string?   TriggerId    { get; set; }
    public string?   ChannelId    { get; set; }
    public char?     EventType    { get; set; }
    public bool?     IsProcessed  { get; set; }
    public DateTime? From         { get; set; }
    public DateTime? To           { get; set; }
    public int       Page         { get; set; } = 1;
    public int       PageSize     { get; set; } = 50;
}
