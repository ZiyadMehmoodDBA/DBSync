using FluentAssertions;
using MSOSync.ConfigurationTests;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Configuration;
using MSOSync.Persistence.Entities;
using Moq;
using Xunit;

namespace MSOSync.ConfigurationTests.Services;

public sealed class ConfigurationTemplateServiceTests : IClassFixture<ConfigurationDbFixture>
{
    private readonly ConfigurationDbFixture _fx;
    private readonly IConfigurationTemplateService _svc;
    private readonly Guid _userId = Guid.NewGuid();

    public ConfigurationTemplateServiceTests(ConfigurationDbFixture fx)
    {
        _fx = fx;
        var validationSvc = new ConfigurationValidationService(fx.Db);
        var auditSvc      = Mock.Of<IAuditService>();
        _svc = new ConfigurationTemplateService(fx.Db, validationSvc, auditSvc);
    }

    private CreateTemplateRequest NewReq(string name = "Test") => new(
        Name: name,
        Description: "desc",
        InitialSettings: new ConfigurationSettings
        {
            HeartbeatIntervalSeconds = 30,
            TransportMode            = "Push",
            MaxRetryAttempts         = 3,
            RetryBackoffSeconds      = 60,
            BatchSizeLimit           = 1000,
            FeatureFlags             = new() { [FeatureFlagCatalog.EnableBulkApply] = true },
            ChannelIds               = [],
            RouterIds                = [],
            TriggerIds               = [],
        });

    [Fact]
    public async Task CreateAsync_CreatesDraftTemplate()
    {
        var dto = await _svc.CreateAsync(NewReq(), _userId, CancellationToken.None);

        dto.Should().NotBeNull();
        dto.Status.Should().Be("Draft");
        dto.LatestDraftVersion.Should().Be(1);
        dto.CurrentPublishedVersion.Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_AfterCreate_SetsImmutableVersion()
    {
        var template = await _svc.CreateAsync(NewReq("Pub1"), _userId, CancellationToken.None);

        var version = await _svc.PublishAsync(template.Id, _userId, CancellationToken.None);

        version.IsDraft.Should().BeFalse();
        version.TemplateContentHash.Should().NotBeNullOrEmpty().And.HaveLength(64);
        version.PublishedAt.Should().NotBeNull();

        var refreshed = await _svc.GetAsync(template.Id, CancellationToken.None);
        refreshed.Status.Should().Be("Published");
        refreshed.CurrentPublishedVersion.Should().Be(1);
        refreshed.LatestDraftVersion.Should().BeNull();
    }

    [Fact]
    public async Task CloneAsync_CreatesDraftFromPublished()
    {
        var t = await _svc.CreateAsync(NewReq("Clone1"), _userId, CancellationToken.None);
        await _svc.PublishAsync(t.Id, _userId, CancellationToken.None);

        var cloned = await _svc.CloneAsync(t.Id, "Clone1-copy", _userId, CancellationToken.None);

        cloned.Status.Should().Be("Draft");
        cloned.Name.Should().Be("Clone1-copy");
        cloned.LatestDraftVersion.Should().Be(1);
    }

    [Fact]
    public async Task ArchiveAsync_Blocked_IfTemplateHasAssignedNode()
    {
        var t = await _svc.CreateAsync(NewReq("Arc1"), _userId, CancellationToken.None);
        await _svc.PublishAsync(t.Id, _userId, CancellationToken.None);

        // Simulate assignment
        var node = new SyncNode
        {
            NodeId                 = "arc-node",
            GroupId                = "g",
            SyncUrl                = "http://x",
            AssignedTemplateId     = t.Id,
            AssignedTemplateVersion = 1,
        };
        _fx.Db.Nodes.Add(node);
        await _fx.Db.SaveChangesAsync();

        var act = async () => await _svc.ArchiveAsync(t.Id, _userId, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*assigned*");
    }

    [Fact]
    public async Task PublishAsync_FailingValidation_Returns422Details()
    {
        var badSettings = new ConfigurationSettings
        {
            HeartbeatIntervalSeconds = 0, // invalid
            TransportMode            = "Push",
            MaxRetryAttempts         = 3,
            RetryBackoffSeconds      = 60,
            BatchSizeLimit           = 1000,
            FeatureFlags             = [],
            ChannelIds               = [],
            RouterIds                = [],
            TriggerIds               = [],
        };
        var t = await _svc.CreateAsync(
            new CreateTemplateRequest("Bad", null, badSettings), _userId, CancellationToken.None);

        var act = async () => await _svc.PublishAsync(t.Id, _userId, CancellationToken.None);
        await act.Should().ThrowAsync<MSOSync.Common.Exceptions.ValidationException>();
    }
}
