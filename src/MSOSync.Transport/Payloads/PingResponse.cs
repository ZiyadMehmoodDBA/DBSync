using MSOSync.Persistence;

namespace MSOSync.Transport.Payloads;

public sealed record PingResponse(
    string        NodeId,
    string        Status,
    TransportMode TransportMode);
