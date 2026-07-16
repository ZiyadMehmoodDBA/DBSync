using MSOSync.Persistence.Models;

namespace MSOSync.Metadata.Configuration;

public sealed record CreateTemplateRequest(
    string Name,
    string? Description,
    ConfigurationSettings InitialSettings);

public sealed record UpdateDraftRequest(ConfigurationSettings Settings);

public sealed record TemplateSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    int? CurrentPublishedVersion,
    int? LatestDraftVersion,
    DateTime UpdatedAt);

public sealed record TemplateDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    int? CurrentPublishedVersion,
    int? LatestDraftVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<TemplateVersionSummaryDto> Versions);

public sealed record TemplateVersionSummaryDto(
    Guid Id,
    int VersionNumber,
    bool IsDraft,
    string? TemplateContentHash,
    int SchemaVersion,
    DateTime? PublishedAt);

public sealed record TemplateVersionDto(
    Guid Id,
    Guid TemplateId,
    int VersionNumber,
    bool IsDraft,
    ConfigurationSettings Settings,
    string? TemplateContentHash,
    int SchemaVersion,
    DateTime? PublishedAt);

public sealed record ValidationPreviewResult(
    ValidationResult Validation,
    string? HashPreview,
    ConfigurationSettings? EffectiveSettings);
