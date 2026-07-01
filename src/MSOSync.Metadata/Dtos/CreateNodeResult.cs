namespace MSOSync.Metadata.Dtos;

public sealed record CreateNodeResult(
    string NodeId,
    string NodeToken,   // raw token — shown once to admin
    NodeDto Node);
