// src/MSOSync.Metadata/NodeManagement/NodeScopeDtos.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public sealed record NodeScopeDto(
    string            NodeId,
    SyncDirection     SyncDirection,
    InitialLoadPolicy InitialLoadPolicy,
    string[]          ChannelIds,
    string[]          TriggerIds,
    string[]          RouterIds
);

public sealed record SetNodeScopeRequest(
    SyncDirection     SyncDirection,
    InitialLoadPolicy InitialLoadPolicy,
    string[]          ChannelIds,
    string[]          TriggerIds,
    string[]          RouterIds
);
