// tests/MSOSync.IntegrationTests/Configuration/ConfigCompareApiTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Configuration;

[Collection("Configuration")]
public sealed class ConfigCompareApiTests(ConfigurationFixture fixture)
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> AdminClientAsync()
    {
        var unauthClient = fixture.CreateClient();
        var token = await fixture.GetJwtAsync(unauthClient, fixture.AdminUsername, fixture.AdminPassword);
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> SeedTemplateWithTwoVersionsAsync(
        string namePrefix, string v1Json, string v2Json)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var templateId = Guid.NewGuid();
        var actorId    = Guid.NewGuid();
        var now        = DateTime.UtcNow;
        var shortId    = templateId.ToString("N")[..8];

        db.ConfigurationTemplates.Add(new SyncConfigurationTemplate
        {
            Id                      = templateId,
            Name                    = $"{namePrefix}-{shortId}",
            Status                  = "Published",
            CurrentPublishedVersion = 2,
            CreatedBy               = actorId,
            CreatedAt               = now,
            UpdatedAt               = now,
        });
        db.ConfigurationTemplateVersions.AddRange(
            new SyncConfigurationTemplateVersion
            {
                Id            = Guid.NewGuid(),
                TemplateId    = templateId,
                VersionNumber = 1,
                IsDraft       = false,
                SettingsJson  = v1Json,
                SchemaVersion = 1,
                PublishedAt   = now,
                PublishedBy   = actorId,
            },
            new SyncConfigurationTemplateVersion
            {
                Id            = Guid.NewGuid(),
                TemplateId    = templateId,
                VersionNumber = 2,
                IsDraft       = false,
                SettingsJson  = v2Json,
                SchemaVersion = 1,
                PublishedAt   = now.AddSeconds(1),
                PublishedBy   = actorId,
            });
        await db.SaveChangesAsync();
        return templateId;
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compare_DifferentVersions_Returns200WithDiffs()
    {
        var templateId = await SeedTemplateWithTwoVersionsAsync(
            "compare",
            """{"host":"old-host","port":5432}""",
            """{"host":"new-host","port":5432,"timeout":30}""");

        var client = await AdminClientAsync();
        var resp   = await client.GetAsync(
            $"api/v1/configuration/templates/{templateId}/compare?v1=1&v2=2");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("hasChanges").GetBoolean().Should().BeTrue();
        body.GetProperty("entries").GetArrayLength().Should().BeGreaterThan(0);
        body.GetProperty("v1Label").GetString().Should().Contain("1");
        body.GetProperty("v2Label").GetString().Should().Contain("2");
    }

    [Fact]
    public async Task Compare_IdenticalVersionContent_Returns200WithNoChanges()
    {
        var json = """{"host":"same","port":5432}""";
        var templateId = await SeedTemplateWithTwoVersionsAsync("identical", json, json);

        var client = await AdminClientAsync();
        var resp   = await client.GetAsync(
            $"api/v1/configuration/templates/{templateId}/compare?v1=1&v2=2");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("hasChanges").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Compare_SameVersionNumber_Returns400()
    {
        var templateId = await SeedTemplateWithTwoVersionsAsync("same-ver", "{}", "{}");

        var client = await AdminClientAsync();
        var resp   = await client.GetAsync(
            $"api/v1/configuration/templates/{templateId}/compare?v1=1&v2=1");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Compare_UnknownTemplate_Returns404()
    {
        var client = await AdminClientAsync();
        var resp   = await client.GetAsync(
            $"api/v1/configuration/templates/{Guid.NewGuid()}/compare?v1=1&v2=2");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Compare_UnknownVersion_Returns404()
    {
        var templateId = await SeedTemplateWithTwoVersionsAsync("unk-ver", "{}", "{}");

        var client = await AdminClientAsync();
        // v2=99 does not exist
        var resp = await client.GetAsync(
            $"api/v1/configuration/templates/{templateId}/compare?v1=1&v2=99");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
