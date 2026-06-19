namespace MSOSync.Metadata.Dtos;

public sealed record ParameterHistoryDto(
    long HistId,
    string ParameterName,
    string? OldValue,
    string? NewValue,
    string? ChangedBy,
    DateTime? ChangeTime);
