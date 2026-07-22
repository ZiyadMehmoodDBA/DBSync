using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Configuration.Dtos;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Configuration;

public sealed class ConfigurationComparisonService(AppDbContext db) : IConfigurationComparisonService
{
    public async Task<ConfigVersionDiffDto> CompareAsync(
        Guid templateId, int v1, int v2, CancellationToken ct = default)
    {
        var versions = await db.ConfigurationTemplateVersions
            .AsNoTracking()
            .Where(v => v.TemplateId == templateId && (v.VersionNumber == v1 || v.VersionNumber == v2))
            .Select(v => new { v.VersionNumber, v.SettingsJson, v.IsDraft, v.PublishedAt })
            .ToListAsync(ct);

        var ver1 = versions.FirstOrDefault(v => v.VersionNumber == v1)
            ?? throw new NotFoundException($"Version {v1} not found for template {templateId}", "VERSION_NOT_FOUND");
        var ver2 = versions.FirstOrDefault(v => v.VersionNumber == v2)
            ?? throw new NotFoundException($"Version {v2} not found for template {templateId}", "VERSION_NOT_FOUND");

        var doc1 = JsonDocument.Parse(ver1.SettingsJson ?? "{}");
        var doc2 = JsonDocument.Parse(ver2.SettingsJson ?? "{}");
        var entries = JsonDiffEngine.Diff(doc1.RootElement, doc2.RootElement);

        return new ConfigVersionDiffDto(
            TemplateId: templateId,
            V1:         v1,
            V2:         v2,
            V1Label:    BuildLabel(v1, ver1.IsDraft, ver1.PublishedAt),
            V2Label:    BuildLabel(v2, ver2.IsDraft, ver2.PublishedAt),
            Entries:    entries,
            HasChanges: entries.Any(e => e.ChangeType != "Unchanged"));
    }

    private static string BuildLabel(int version, bool isDraft, DateTime? publishedAt)
    {
        if (isDraft) return $"v{version} (Draft)";
        return publishedAt.HasValue
            ? $"v{version} (Published {publishedAt.Value:yyyy-MM-dd})"
            : $"v{version}";
    }
}
