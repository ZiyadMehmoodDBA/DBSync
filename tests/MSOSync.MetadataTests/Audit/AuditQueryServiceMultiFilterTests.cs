using FluentAssertions;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Audit;

public sealed class AuditQueryServiceMultiFilterTests : IDisposable
{
    private readonly AppDbContext   _db     = TestDbContext.Create();
    private readonly CursorSigner   _signer = new(new byte[32]);

    public void Dispose() => _db.Dispose();

    private AuditQueryService BuildSvc()
        => new(new TestPlatformRepository<SyncAudit>(_db), _signer);

    private async Task SeedAsync(string? username, string? actionName, string? objectName)
    {
        _db.Audits.Add(new SyncAudit
        {
            Username   = username,
            ActionName = actionName,
            ObjectName = objectName,
            CreateTime = DateTime.UtcNow,
            TenantId   = Guid.Empty,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetAuditsAsync_Usernames_array_filters_by_multiple_users()
    {
        await SeedAsync("alice", "NODE_APPROVED", "n1");
        await SeedAsync("bob",   "NODE_APPROVED", "n2");
        await SeedAsync("carol", "NODE_APPROVED", "n3");

        var svc = BuildSvc();
        var result = await svc.GetAuditsAsync(new AuditFilter
        {
            Usernames = ["alice", "bob"],
            PageSize  = 50,
        }, default);

        result.Items.Should().HaveCount(2);
        result.Items.Select(r => r.Username).Should().BeEquivalentTo(["alice", "bob"]);
    }

    [Fact]
    public async Task GetAuditsAsync_ActionNames_array_filters_by_multiple_actions()
    {
        await SeedAsync("u1", "NODE_APPROVED",  "n1");
        await SeedAsync("u2", "NODE_DISABLED",  "n2");
        await SeedAsync("u3", "NODE_HEARTBEAT", "n3");

        var svc = BuildSvc();
        var result = await svc.GetAuditsAsync(new AuditFilter
        {
            ActionNames = ["NODE_APPROVED", "NODE_DISABLED"],
            PageSize    = 50,
        }, default);

        result.Items.Should().HaveCount(2);
        result.Items.Select(r => r.ActionName).Should()
            .BeEquivalentTo(["NODE_APPROVED", "NODE_DISABLED"]);
    }

    [Fact]
    public async Task GetAuditsAsync_ObjectNames_array_filters_by_multiple_objects()
    {
        await SeedAsync("u1", "NODE_APPROVED", "node-a");
        await SeedAsync("u2", "NODE_APPROVED", "node-b");
        await SeedAsync("u3", "NODE_APPROVED", "node-c");

        var svc = BuildSvc();
        var result = await svc.GetAuditsAsync(new AuditFilter
        {
            ObjectNames = ["node-a", "node-c"],
            PageSize    = 50,
        }, default);

        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAuditsAsync_multi_value_takes_precedence_over_single_value()
    {
        await SeedAsync("alice", "NODE_APPROVED", "n1");
        await SeedAsync("bob",   "NODE_APPROVED", "n2");

        var svc = BuildSvc();
        var result = await svc.GetAuditsAsync(new AuditFilter
        {
            Username  = "carol",             // single-value (ignored when multi is set)
            Usernames = ["alice"],            // multi-value takes precedence
            PageSize  = 50,
        }, default);

        result.Items.Should().HaveCount(1);
        result.Items[0].Username.Should().Be("alice");
    }

    [Fact]
    public async Task GetEntityHistoryAsync_returns_events_for_objectName()
    {
        await SeedAsync("u1", "NODE_APPROVED",  "target-node");
        await SeedAsync("u2", "NODE_DISABLED",  "target-node");
        await SeedAsync("u3", "NODE_HEARTBEAT", "other-node");

        var svc = BuildSvc();
        var result = await svc.GetEntityHistoryAsync("target-node", null, 50);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(r => r.ObjectName == "target-node");
    }

    [Fact]
    public async Task GetEntityHistoryAsync_returns_empty_for_unknown_objectName()
    {
        var svc = BuildSvc();
        var result = await svc.GetEntityHistoryAsync("does-not-exist", null, 50);
        result.Items.Should().BeEmpty();
    }
}
