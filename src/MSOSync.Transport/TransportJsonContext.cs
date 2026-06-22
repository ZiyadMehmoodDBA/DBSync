using System.Text.Json.Serialization;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

[JsonSerializable(typeof(EventPayload))]
[JsonSerializable(typeof(BatchPayload))]
[JsonSerializable(typeof(PullRequest))]
[JsonSerializable(typeof(PullResponse))]
[JsonSerializable(typeof(AckPayload))]
[JsonSerializable(typeof(PushResponse))]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(List<BatchPayload>))]
[JsonSerializable(typeof(List<EventPayload>))]
public partial class TransportJsonContext : JsonSerializerContext { }
