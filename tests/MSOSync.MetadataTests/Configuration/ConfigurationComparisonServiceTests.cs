using FluentAssertions;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Configuration;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Configuration;

public sealed class ConfigurationComparisonServiceTests : IDisposable
{
    private readonly MSOSync.Persistence.AppDbContext _db = TestDbContext.Create();

    public void Dispose() => _db.Dispose();

    private async Task<Guid> SeedTemplateAsync(string name)
    {
        var id = Guid.NewGuid();
        _db.ConfigurationTemplates.Add(new SyncConfigurationTemplate
        {
            Id = id, Name = name, Description = name,
            LatestDraftVersion = null, CurrentPublishedVersion = null,
            TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task SeedVersionAsync(Guid templateId, int version, string settingsJson, bool isDraft = false)
    {
        _db.ConfigurationTemplateVersions.Add(new SyncConfigurationTemplateVersion
        {
            Id = Guid.NewGuid(), TemplateId = templateId,
            VersionNumber = version, SettingsJson = settingsJson,
            IsDraft = isDraft, SchemaVersion = 1,
            TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task CompareAsync_returns_diff_for_valid_versions()
    {
        var templateId = await SeedTemplateAsync("t1");
        await SeedVersionAsync(templateId, 1, """{"host":"s1"}""");
        await SeedVersionAsync(templateId, 2, """{"host":"s2"}""");

        var svc = new ConfigurationComparisonService(_db);
        var result = await svc.CompareAsync(templateId, 1, 2);

        result.TemplateId.Should().Be(templateId);
        result.V1.Should().Be(1);
        result.V2.Should().Be(2);
        result.HasChanges.Should().BeTrue();
        result.Entries.Should().Contain(e => e.Key == "host" && e.ChangeType == "Changed");
    }

    [Fact]
    public async Task CompareAsync_throws_NotFoundException_when_version_missing()
    {
        var templateId = await SeedTemplateAsync("t2");
        await SeedVersionAsync(templateId, 1, """{"host":"s1"}""");

        var svc = new ConfigurationComparisonService(_db);
        var act = async () => await svc.CompareAsync(templateId, 1, 99);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CompareAsync_HasChanges_false_when_identical()
    {
        var templateId = await SeedTemplateAsync("t3");
        await SeedVersionAsync(templateId, 1, """{"host":"s1"}""");
        await SeedVersionAsync(templateId, 2, """{"host":"s1"}""");

        var svc = new ConfigurationComparisonService(_db);
        var result = await svc.CompareAsync(templateId, 1, 2);
        result.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task CompareAsync_generates_readable_version_labels()
    {
        var templateId = await SeedTemplateAsync("t4");
        await SeedVersionAsync(templateId, 1, """{}""", isDraft: false);
        await SeedVersionAsync(templateId, 2, """{}""", isDraft: true);

        var svc = new ConfigurationComparisonService(_db);
        var result = await svc.CompareAsync(templateId, 1, 2);
        result.V1Label.Should().Contain("v1");
        result.V2Label.Should().Contain("v2");
        result.V2Label.ToLowerInvariant().Should().Contain("draft");
    }

    [Fact]
    public async Task CompareAsync_throws_NotFoundException_when_version_belongs_to_different_template()
    {
        var templateId1 = await SeedTemplateAsync("t5a");
        var templateId2 = await SeedTemplateAsync("t5b");
        await SeedVersionAsync(templateId1, 1, """{}""");
        await SeedVersionAsync(templateId2, 2, """{}"""); // different template

        var svc = new ConfigurationComparisonService(_db);
        var act = async () => await svc.CompareAsync(templateId1, 1, 2);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
