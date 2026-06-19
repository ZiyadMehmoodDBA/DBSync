namespace MSOSync.Metadata.Dtos;

public sealed record ParameterDescriptorDto(
    string Name,
    string Description,
    bool IsSecret,
    bool RequiresRestart,
    bool IsDynamic);
