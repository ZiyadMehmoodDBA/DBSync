// tests/MSOSync.IntegrationTests/MultiTenancy/CrossTenantIsolationTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MSOSync.IntegrationTests.MultiTenancy;

[Collection("MultiTenancy")]
[Trait("Category", "MultiTenancy")]
public sealed class CrossTenantIsolationTests(MultiTenantFixture fixture)
{
    [Fact]
    public async Task CrossTenantIsolation_Node_Returns404()
    {
        // TenantA context — cannot see TenantB's node
        var nodeBVisible = await fixture.WithTenantAsync(fixture.TenantAId, async () =>
            await fixture.Db.Nodes.AnyAsync(n => n.NodeId == fixture.NodeBId));

        nodeBVisible.Should().BeFalse(
            "global query filter must hide TenantB's node from TenantA context");
    }

    [Fact]
    public async Task CrossTenantIsolation_Channel_Returns404()
    {
        // TenantB context — cannot see TenantA's channel
        var channelAVisible = await fixture.WithTenantAsync(fixture.TenantBId, async () =>
            await fixture.Db.Channels.AnyAsync(c => c.ChannelId == fixture.ChannelAId));

        channelAVisible.Should().BeFalse(
            "global query filter must hide TenantA's channel from TenantB context");
    }

    [Fact]
    public async Task SameTenant_Node_IsVisible()
    {
        // TenantA context — can see its own node
        var nodeAVisible = await fixture.WithTenantAsync(fixture.TenantAId, async () =>
            await fixture.Db.Nodes.AnyAsync(n => n.NodeId == fixture.NodeAId));

        nodeAVisible.Should().BeTrue(
            "tenant must be able to see its own nodes");
    }

    [Fact]
    public async Task PlatformContext_CanSeeAllTenantNodes()
    {
        // Platform context (accessor.TenantId == null) — sees all nodes
        // IgnoreQueryFilters() as belt-and-suspenders to confirm both nodes exist
        var nodeAVisible = await fixture.Db.Nodes.IgnoreQueryFilters()
            .AnyAsync(n => n.NodeId == fixture.NodeAId);
        var nodeBVisible = await fixture.Db.Nodes.IgnoreQueryFilters()
            .AnyAsync(n => n.NodeId == fixture.NodeBId);

        nodeAVisible.Should().BeTrue("platform context must see TenantA node");
        nodeBVisible.Should().BeTrue("platform context must see TenantB node");
    }

    [Fact]
    public async Task TenantAContext_NodeCount_DoesNotIncludeTenantBNodes()
    {
        var platformCount = await fixture.Db.Nodes.IgnoreQueryFilters().CountAsync();

        var tenantACount = await fixture.WithTenantAsync(fixture.TenantAId, async () =>
            await fixture.Db.Nodes.CountAsync());

        // TenantA sees fewer nodes than the platform (because TenantB's node is hidden)
        tenantACount.Should().BeLessThan(platformCount,
            "tenant context must not see other tenants' nodes");
    }
}
