namespace MSOSync.Metadata.Dtos;

public sealed record TriggerHistDto(
    long HistId,
    string TriggerId,
    string? DdlText,
    int? TriggerVersion,
    DateTime? CreateTime);
