using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Configuration;

public sealed class ConfigurationTemplateService(
    AppDbContext db,
    IConfigurationValidationService validationSvc,
    IAuditService auditSvc) : IConfigurationTemplateService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<TemplateDto> CreateAsync(CreateTemplateRequest req, Guid userId, CancellationToken ct)
    {
        if (await db.ConfigurationTemplates.AnyAsync(t => t.Name == req.Name, ct))
            throw new DuplicateEntityException($"Template '{req.Name}' already exists");

        var now = DateTime.UtcNow;
        var template = new SyncConfigurationTemplate
        {
            Id                 = Guid.NewGuid(),
            Name               = req.Name,
            Description        = req.Description,
            Status             = "Draft",
            LatestDraftVersion = 1,
            CreatedBy          = userId,
            CreatedAt          = now,
            UpdatedAt          = now,
        };
        db.ConfigurationTemplates.Add(template);

        var version = new SyncConfigurationTemplateVersion
        {
            Id            = Guid.NewGuid(),
            TemplateId    = template.Id,
            VersionNumber = 1,
            IsDraft       = true,
            SettingsJson  = JsonSerializer.Serialize(req.InitialSettings, _json),
            SchemaVersion = 1,
        };
        db.ConfigurationTemplateVersions.Add(version);
        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(
            ConfigurationAuditConstants.TemplateCreated,
            $"Configuration template '{req.Name}' created",
            userId.ToString(), ct);

        return await GetAsync(template.Id, ct);
    }

    public async Task<TemplateVersionDto> UpdateDraftAsync(
        Guid templateId, UpdateDraftRequest req, byte[] rowVersion, Guid userId, CancellationToken ct)
    {
        var template = await db.ConfigurationTemplates.FindAsync([templateId], ct)
            ?? throw new NotFoundException($"Template {templateId} not found");

        var draft = await db.ConfigurationTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == templateId && v.IsDraft, ct)
            ?? throw new NotFoundException($"No draft version for template {templateId}");

        if (draft.RowVersion != null && rowVersion != null && !draft.RowVersion.SequenceEqual(rowVersion))
            throw new ConflictException("Draft was modified by another user");

        draft.SettingsJson = JsonSerializer.Serialize(req.Settings, _json);
        template.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(
            ConfigurationAuditConstants.TemplateDraftUpdated,
            $"Draft updated for template '{template.Name}' v{draft.VersionNumber}",
            userId.ToString(), ct);

        return MapVersion(draft);
    }

    public async Task<ValidationPreviewResult> ValidatePreviewAsync(Guid templateId, CancellationToken ct)
    {
        var draft = await db.ConfigurationTemplateVersions
            .FirstOrDefaultAsync(v => v.TemplateId == templateId && v.IsDraft, ct)
            ?? throw new NotFoundException($"No draft version for template {templateId}");

        var settings = JsonSerializer.Deserialize<ConfigurationSettings>(draft.SettingsJson, _json)!;
        var validation = await validationSvc.ValidateAsync(settings, ct);

        string? hashPreview = null;
        if (validation.IsValid)
            hashPreview = CanonicalJsonSerializer.ComputeHash(settings);

        return new ValidationPreviewResult(validation, hashPreview, validation.IsValid ? settings : null);
    }

    public async Task<TemplateVersionDto> PublishAsync(Guid templateId, Guid userId, CancellationToken ct)
    {
        var template = await db.ConfigurationTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException($"Template {templateId} not found");

        var draft = template.Versions.FirstOrDefault(v => v.IsDraft)
            ?? throw new NotFoundException($"No draft version for template {templateId}");

        var settings = JsonSerializer.Deserialize<ConfigurationSettings>(draft.SettingsJson, _json)!;
        var validation = await validationSvc.ValidateAsync(settings, ct);

        if (!validation.IsValid)
        {
            var msg = string.Join("; ", validation.Errors.Select(e => $"{e.Field}: {e.Message}"));
            throw new ValidationException($"Template settings failed validation: {msg}");
        }

        var now         = DateTime.UtcNow;
        var contentHash = CanonicalJsonSerializer.ComputeHash(settings);

        draft.IsDraft             = false;
        draft.TemplateContentHash = contentHash;
        draft.PublishedAt         = now;
        draft.PublishedBy         = userId;

        template.CurrentPublishedVersion = draft.VersionNumber;
        template.LatestDraftVersion      = null;
        template.Status                  = "Published";
        template.UpdatedAt               = now;

        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(
            ConfigurationAuditConstants.TemplatePublished,
            $"Template '{template.Name}' published as v{draft.VersionNumber} (hash: {contentHash[..8]}…)",
            userId.ToString(), ct);

        return MapVersion(draft);
    }

    public async Task<TemplateDto> CloneAsync(Guid templateId, string newName, Guid userId, CancellationToken ct)
    {
        var source = await db.ConfigurationTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException($"Template {templateId} not found");

        var latestPublished = source.Versions
            .Where(v => !v.IsDraft)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Can only clone a Published template");

        if (await db.ConfigurationTemplates.AnyAsync(t => t.Name == newName, ct))
            throw new DuplicateEntityException($"Template '{newName}' already exists");

        var now = DateTime.UtcNow;
        var newTemplate = new SyncConfigurationTemplate
        {
            Id                 = Guid.NewGuid(),
            Name               = newName,
            Description        = source.Description,
            Status             = "Draft",
            LatestDraftVersion = 1,
            CreatedBy          = userId,
            CreatedAt          = now,
            UpdatedAt          = now,
        };
        db.ConfigurationTemplates.Add(newTemplate);

        var newVersion = new SyncConfigurationTemplateVersion
        {
            Id            = Guid.NewGuid(),
            TemplateId    = newTemplate.Id,
            VersionNumber = 1,
            IsDraft       = true,
            SettingsJson  = latestPublished.SettingsJson,
            SchemaVersion = latestPublished.SchemaVersion,
        };
        db.ConfigurationTemplateVersions.Add(newVersion);
        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(
            ConfigurationAuditConstants.TemplateCloned,
            $"Template '{source.Name}' cloned as '{newName}'",
            userId.ToString(), ct);

        return await GetAsync(newTemplate.Id, ct);
    }

    public async Task ArchiveAsync(Guid templateId, Guid userId, CancellationToken ct)
    {
        var template = await db.ConfigurationTemplates.FindAsync([templateId], ct)
            ?? throw new NotFoundException($"Template {templateId} not found");

        bool isAssigned = await db.Nodes.AnyAsync(n => n.AssignedTemplateId == templateId, ct);
        if (isAssigned)
            throw new InvalidOperationException(
                "Cannot archive template: one or more nodes have it assigned");

        bool hasActiveRollout = await db.ConfigurationRollouts.AnyAsync(
            r => r.TemplateId == templateId && (r.Status == "Queued" || r.Status == "InProgress"), ct);
        if (hasActiveRollout)
            throw new InvalidOperationException(
                "Cannot archive template: an active rollout is in progress");

        template.Status    = "Archived";
        template.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditSvc.WriteAsync(
            ConfigurationAuditConstants.TemplateArchived,
            $"Template '{template.Name}' archived",
            userId.ToString(), ct);
    }

    public async Task<TemplateDto> GetAsync(Guid templateId, CancellationToken ct)
    {
        var template = await db.ConfigurationTemplates
            .AsNoTracking()
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException($"Template {templateId} not found");

        return MapTemplate(template);
    }

    public async Task<IReadOnlyList<TemplateSummaryDto>> ListAsync(string? statusFilter, CancellationToken ct)
    {
        var query = db.ConfigurationTemplates.AsNoTracking();
        if (!string.IsNullOrEmpty(statusFilter))
            query = query.Where(t => t.Status == statusFilter);

        return await query
            .OrderBy(t => t.Name)
            .Select(t => new TemplateSummaryDto(
                t.Id, t.Name, t.Description, t.Status,
                t.CurrentPublishedVersion, t.LatestDraftVersion, t.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<TemplateVersionDto> GetVersionAsync(Guid templateId, int version, CancellationToken ct)
    {
        var v = await db.ConfigurationTemplateVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TemplateId == templateId && x.VersionNumber == version, ct)
            ?? throw new NotFoundException($"Template {templateId} version {version} not found");
        return MapVersion(v);
    }

    public async Task<IReadOnlyList<TemplateVersionSummaryDto>> ListVersionsAsync(Guid templateId, CancellationToken ct)
    {
        return await db.ConfigurationTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new TemplateVersionSummaryDto(
                v.Id, v.VersionNumber, v.IsDraft, v.TemplateContentHash, v.SchemaVersion, v.PublishedAt))
            .ToListAsync(ct);
    }

    private static TemplateDto MapTemplate(SyncConfigurationTemplate t) => new(
        t.Id, t.Name, t.Description, t.Status,
        t.CurrentPublishedVersion, t.LatestDraftVersion,
        t.CreatedAt, t.UpdatedAt,
        t.Versions
            .Select(v => new TemplateVersionSummaryDto(
                v.Id, v.VersionNumber, v.IsDraft, v.TemplateContentHash, v.SchemaVersion, v.PublishedAt))
            .OrderByDescending(v => v.VersionNumber)
            .ToList());

    private static TemplateVersionDto MapVersion(SyncConfigurationTemplateVersion v)
    {
        var settings = JsonSerializer.Deserialize<ConfigurationSettings>(v.SettingsJson, _json)!;
        return new(v.Id, v.TemplateId, v.VersionNumber, v.IsDraft, settings,
            v.TemplateContentHash, v.SchemaVersion, v.PublishedAt);
    }
}
