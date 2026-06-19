namespace MSOSync.Metadata.Dtos;

public sealed record ParameterDto(
    string Name,
    string? Value,
    string Description,
    bool IsSecret,
    bool RequiresRestart,
    bool IsDynamic);
