using MSOSync.Metadata.Configuration;

namespace MSOSync.Api.Dtos.Configuration;

public sealed record TemplateVersionDiffResponse(TemplateVersionDto Version1, TemplateVersionDto Version2);
