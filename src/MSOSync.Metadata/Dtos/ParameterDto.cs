namespace MSOSync.Metadata.Dtos;

public sealed record ParameterDto(
    string  ParameterName,
    string? ParameterValue,
    string? Category,
    string? DisplayName,
    string? Description,
    int?    DisplayOrder,
    string? ValueType,
    string? MinimumValue,
    string? MaximumValue,
    string? AllowedValues,
    bool    IsSecret,
    bool    IsDynamic,
    bool    RequiresRestart);
