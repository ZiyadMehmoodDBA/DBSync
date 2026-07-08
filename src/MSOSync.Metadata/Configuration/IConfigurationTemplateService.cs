namespace MSOSync.Metadata.Configuration;

public interface IConfigurationTemplateService
{
    Task<TemplateDto> CreateAsync(CreateTemplateRequest request, Guid userId, CancellationToken ct);
    Task<TemplateVersionDto> UpdateDraftAsync(Guid templateId, UpdateDraftRequest request, byte[] rowVersion, Guid userId, CancellationToken ct);
    Task<ValidationPreviewResult> ValidatePreviewAsync(Guid templateId, CancellationToken ct);
    Task<TemplateVersionDto> PublishAsync(Guid templateId, Guid userId, CancellationToken ct);
    Task<TemplateDto> CloneAsync(Guid templateId, string newName, Guid userId, CancellationToken ct);
    Task ArchiveAsync(Guid templateId, Guid userId, CancellationToken ct);
    Task<TemplateDto> GetAsync(Guid templateId, CancellationToken ct);
    Task<IReadOnlyList<TemplateSummaryDto>> ListAsync(string? statusFilter, CancellationToken ct);
    Task<TemplateVersionDto> GetVersionAsync(Guid templateId, int version, CancellationToken ct);
    Task<IReadOnlyList<TemplateVersionSummaryDto>> ListVersionsAsync(Guid templateId, CancellationToken ct);
}
