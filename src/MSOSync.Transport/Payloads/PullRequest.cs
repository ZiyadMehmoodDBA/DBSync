namespace MSOSync.Transport.Payloads;

public sealed record PullRequest(
    string TargetNodeId,
    string ChannelId,
    long   AfterSequence);
