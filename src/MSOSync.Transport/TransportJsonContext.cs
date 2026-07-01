using System.Text.Json.Serialization;
using MSOSync.Engine;
using MSOSync.Metadata.Dtos;
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
[JsonSerializable(typeof(HeartbeatRequest))]
public partial class TransportJsonContext : JsonSerializerContext { }
