namespace MSOSync.Api.Dtos.Export;

public sealed record CreateExportJobRequest(
    string ResourceType,
    string Format,
    string FiltersJson,
    Guid?  ParentJobId = null
);
