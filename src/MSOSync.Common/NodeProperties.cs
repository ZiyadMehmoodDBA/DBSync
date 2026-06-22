using System.Text.Json.Serialization;

namespace MSOSync.Common;

public sealed class NodeProperties
{
    public string NodeId    { get; init; } = null!;
    public string GroupId   { get; init; } = null!;
    public string SyncUrl   { get; init; } = null!;

    [JsonIgnore]
    public string NodeToken { get; init; } = null!;
}
