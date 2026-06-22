// src/MSOSync.Trigger/TriggerVerifyResult.cs
namespace MSOSync.Trigger;

public sealed record TriggerVerifyResult(
    string TriggerId,
    string NodeId,
    TriggerDriftStatus Status,
    int? InstalledVersion,
    int MetadataVersion,
    string? Message);
