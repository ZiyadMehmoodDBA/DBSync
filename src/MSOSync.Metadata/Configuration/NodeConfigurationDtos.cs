using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed record CurrentConfigDto(
    Guid   TemplateId,
    int    TemplateVersion,
    string ContentHash,           // TemplateContentHash (from template version)
    int    ConfigurationVersion,  // same as TemplateVersion in CE
    int    SchemaVersion,
    ConfigurationSettings Effective);

public sealed record CurrentConfigResult(
    bool              NotModified,
    CurrentConfigDto? Config,
    string?           ETag);
