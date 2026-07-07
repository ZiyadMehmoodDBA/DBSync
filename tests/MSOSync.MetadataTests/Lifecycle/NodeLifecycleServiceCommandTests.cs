using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class NodeLifecycleServiceCommandTests
{
    private sealed class Fixture
    {
        public AppDbContext Db { get; }
        public NodeLifecycleService Svc { get; }
        public List<(string Action, string Detail)> Audits { get; } = [];
        public List<object> Published { get; } = [];
        public IBootstrapTokenService BootstrapTokens { get; }

        public Fixture(Action<Mock<IMediator>, AppDbContext>? configureMediator = null)
        {
            Db = TestDbContext.Create();

            var auditMock = new Mock<IAuditService>();
            auditMock
                .Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, CancellationToken>((action, detail, _, _) => Audits.Add((action, detail)))
                .Returns(Task.CompletedTask);

            var mediatorMock = new Mock<IMediator>();
            mediatorMock
                .Setup(m => m.Publish(It.IsAny<NodeLifecycleChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback<NodeLifecycleChangedEvent, CancellationToken>((n, _) => Published.Add(n))
                .Returns(Task.CompletedTask);
            mediatorMock
                .Setup(m => m.Publish(It.IsAny<NodeMaintenanceChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback<NodeMaintenanceChangedEvent, CancellationToken>((n, _) => Published.Add(n))
                .Returns(Task.CompletedTask);
            configureMediator?.Invoke(mediatorMock, Db);

            var options = Options.Create(new LifecycleOptions());
            var hasher  = new BCryptPasswordHasher();
            BootstrapTokens = new BootstrapTokenService(Db, hasher, options);

            Svc = new NodeLifecycleService(
                Db,
                new RegistrationDiffService(),
                auditMock.Object,
                mediatorMock.Object,
                new NodeLifecycleStateMachine(),
                new NodeLifecycleHistoryService(Db),
                BootstrapTokens,
                new NodeSecurityService(Db, hasher),
                new NodeLifecycleLockRegistry(),
                options,
                new ConfigurationBuilder().Build(),
                NullLogger<NodeLifecycleService>.Instance);
        }

        public async Task<SyncNode> SeedNodeAsync(
            string nodeId, NodeLifecycleState state, string? externalId = null,
            NodeLifecycleState? previousState = null)
        {
            var node = new SyncNode
            {
                NodeId = nodeId, GroupId = "g1", SyncUrl = "http://n",
                NodeName = nodeId, ExternalId = externalId ?? string.Empty,
                LifecycleState = state, PreviousLifecycleState = previousState,
            };
            Db.Nodes.Add(node);
            await Db.SaveChangesAsync();
            return node;
        }
    }

    // ── Enable / Disable ────────────────────────────────────────────────────────

    [Fact]
    public async Task Enable_FromDisabled_TransitionsToActive_WritesHistoryAndAudit_PublishesEvent()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Disabled);

        await f.Svc.EnableAsync("n1", "admin");

        (await f.Db.Nodes.FindAsync("n1"))!.LifecycleState.Should().Be(NodeLifecycleState.Active);
        var row = f.Db.NodeLifecycleHistories.Single();
        row.FromState.Should().Be(NodeLifecycleState.Disabled);
        row.ToState.Should().Be(NodeLifecycleState.Active);
        row.Trigger.Should().Be(LifecycleTrigger.Manual);
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeEnabled);
        f.Published.OfType<NodeLifecycleChangedEvent>().Should().ContainSingle(e =>
            e.NodeId == "n1" && e.NewState == NodeLifecycleState.Active);
    }

    [Fact]
    public async Task Enable_FromActive_ThrowsInvalidLifecycleTransition()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Active);

        var act = () => f.Svc.EnableAsync("n1", "admin");

        await act.Should().ThrowAsync<InvalidLifecycleTransitionException>();
    }

    [Fact]
    public async Task Disable_FromActive_TransitionsToDisabled()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Active);

        await f.Svc.DisableAsync("n1", "maintenance window over", "admin");

        (await f.Db.Nodes.FindAsync("n1"))!.LifecycleState.Should().Be(NodeLifecycleState.Disabled);
    }

    [Fact]
    public async Task Pipeline_HistoryAuditEvent_ShareOneCorrelationId()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Disabled);

        await f.Svc.EnableAsync("n1", "admin");

        var evt = f.Published.OfType<NodeLifecycleChangedEvent>().Single();
        var row = f.Db.NodeLifecycleHistories.Single();
        row.CorrelationId.Should().Be(evt.CorrelationId);
        f.Audits.Single(a => a.Action == NodeManagementAuditActions.NodeEnabled)
            .Detail.Should().Contain(evt.CorrelationId.ToString());
    }

    // ── Activation ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Activate_PendingRegistration_HappyPath_ReturnsTokenAndTransitionsActive()
    {
        var f = new Fixture();
        var provision = await f.Svc.ProvisionAsync(
            new ProvisionRequestDto("Node1", "ext-1", "target", "srv", "dbn", null, null), "admin");

        var result = await f.Svc.ActivateAsync("ext-1", provision.Token, "1.2.3");

        result.NodeToken.Should().NotBeNullOrWhiteSpace();
        result.HeartbeatIntervalSeconds.Should().Be(30);
        result.ProbeIntervalSeconds.Should().Be(60);
        result.ConfigurationVersion.Should().Be(1);

        var node = await f.Db.Nodes.SingleAsync(n => n.ExternalId == "ext-1");
        node.LifecycleState.Should().Be(NodeLifecycleState.Active);
        node.RegistrationTime.Should().NotBeNull();
        f.Db.NodeSecurities.Should().ContainSingle(s => s.NodeId == node.NodeId);
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeActivated);
    }

    [Fact]
    public async Task Activate_ConsumedToken_ThrowsUnauthorized()
    {
        var f = new Fixture();
        var provision = await f.Svc.ProvisionAsync(
            new ProvisionRequestDto("Node1", "ext-1", "target", "srv", "dbn", null, null), "admin");

        // Simulate replay: token consumed but node still PendingRegistration.
        var token = f.Db.NodeBootstrapTokens.Single();
        token.ConsumedAt = DateTimeOffset.UtcNow;
        await f.Db.SaveChangesAsync();

        var act = () => f.Svc.ActivateAsync("ext-1", provision.Token, "1.2.3");

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Activate_RevokedToken_ThrowsUnauthorized()
    {
        var f = new Fixture();
        var provision = await f.Svc.ProvisionAsync(
            new ProvisionRequestDto("Node1", "ext-1", "target", "srv", "dbn", null, null), "admin");

        var token = f.Db.NodeBootstrapTokens.Single();
        token.RevokedAt = DateTimeOffset.UtcNow;
        await f.Db.SaveChangesAsync();

        var act = () => f.Svc.ActivateAsync("ext-1", provision.Token, "1.2.3");

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Activate_WrongState_Disabled_ThrowsInvalidLifecycleTransition()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Disabled, externalId: "ext-1");

        var act = () => f.Svc.ActivateAsync("ext-1", "whatever", "1.2.3");

        await act.Should().ThrowAsync<InvalidLifecycleTransitionException>();
    }

    [Fact]
    public async Task Activate_Recovery_ClearsPreviousLifecycleState_AuditsRecoveryActivated()
    {
        var f = new Fixture();
        var node = await f.SeedNodeAsync("n1", NodeLifecycleState.Recovery,
            externalId: "ext-1", previousState: NodeLifecycleState.Disabled);
        var raw = await f.BootstrapTokens.IssueAsync(node.NodeId, "admin");
        await f.Db.SaveChangesAsync();

        await f.Svc.ActivateAsync("ext-1", raw, "2.0.0");

        var reloaded = await f.Db.Nodes.FindAsync("n1");
        reloaded!.LifecycleState.Should().Be(NodeLifecycleState.Active);
        reloaded.PreviousLifecycleState.Should().BeNull();   // Invariant 4
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeRecoveryActivated);
    }

    // ── Recovery entry / approval / rejection ──────────────────────────────────

    [Fact]
    public async Task Register_KnownExternalId_EntersRecovery_StoresPreviousLifecycleState()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Disabled, externalId: "ext-1");

        var id = await f.Svc.RegisterAsync(new InboundRegistrationDto("ext-1", "Node1", "target", null));

        f.Db.RegistrationRequests.Find(id)!.RegistrationType.Should().Be(RegistrationType.Recovery);
        var node = await f.Db.Nodes.FindAsync("n1");
        node!.LifecycleState.Should().Be(NodeLifecycleState.Recovery);
        node.PreviousLifecycleState.Should().Be(NodeLifecycleState.Disabled);   // Invariant 4
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeRecoveryRequested);
    }

    [Fact]
    public async Task Register_AlreadyInRecovery_NoOps()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Recovery,
            externalId: "ext-1", previousState: NodeLifecycleState.Active);

        await f.Svc.RegisterAsync(new InboundRegistrationDto("ext-1", "Node1", "target", null));

        var node = await f.Db.Nodes.FindAsync("n1");
        node!.LifecycleState.Should().Be(NodeLifecycleState.Recovery);
        node.PreviousLifecycleState.Should().Be(NodeLifecycleState.Active);   // untouched
        f.Published.OfType<NodeLifecycleChangedEvent>().Should().BeEmpty();   // Invariant 11: no transition fired
        f.Db.NodeLifecycleHistories.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveRegistration_New_CreatesSyncNodeInPendingRegistration()
    {
        var f = new Fixture();
        var id = await f.Svc.RegisterAsync(new InboundRegistrationDto("ext-9", "Node9", "target", null));

        var result = await f.Svc.ApproveAsync(id, "ok", "admin");

        result.BootstrapToken.Should().BeNull();
        var node = await f.Db.Nodes.SingleAsync(n => n.ExternalId == "ext-9");
        node.LifecycleState.Should().Be(NodeLifecycleState.PendingRegistration);
        f.Db.NodeLifecycleHistories.Should().ContainSingle(h =>
            h.NodeId == node.NodeId && h.FromState == null
            && h.ToState == NodeLifecycleState.PendingRegistration
            && h.Trigger == LifecycleTrigger.Registration);
    }

    [Fact]
    public async Task ApproveRecovery_RevokesCredentials_IssuesBootstrapToken_NoStateChange()
    {
        var f = new Fixture();
        var node = await f.SeedNodeAsync("n1", NodeLifecycleState.Disabled, externalId: "ext-1");
        f.Db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId = node.NodeId, CurrentTokenHash = "old-hash", CreatedTime = DateTime.UtcNow
        });
        await f.Db.SaveChangesAsync();

        var id = await f.Svc.RegisterAsync(new InboundRegistrationDto("ext-1", "Node1", "target", null));
        var result = await f.Svc.ApproveAsync(id, null, "admin");

        result.BootstrapToken.Should().NotBeNullOrWhiteSpace();   // returned once to the operator
        f.Db.NodeSecurities.Should().BeEmpty();                   // old identity dead
        f.Db.NodeBootstrapTokens.Should().ContainSingle(t =>
            t.NodeId == node.NodeId && t.ConsumedAt == null && t.RevokedAt == null);
        (await f.Db.Nodes.FindAsync("n1"))!.LifecycleState
            .Should().Be(NodeLifecycleState.Recovery);            // stays Recovery until activation
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeRecoveryApproved);
    }

    [Fact]
    public async Task RejectRecovery_ReturnsToPreviousState_ClearsIt()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Active, externalId: "ext-1");
        await f.Svc.DisableAsync("n1", null, "admin");   // Disabled — recovery must return here

        var id = await f.Svc.RegisterAsync(new InboundRegistrationDto("ext-1", "Node1", "target", null));
        await f.Svc.RejectAsync(id, "not this one", "admin");

        var node = await f.Db.Nodes.FindAsync("n1");
        node!.LifecycleState.Should().Be(NodeLifecycleState.Disabled);
        node.PreviousLifecycleState.Should().BeNull();
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeRecoveryRejected);
    }

    // ── Maintenance ────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartMaintenance_OnActive_SetsColumns_AuditsStarted()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Active);
        var until = DateTimeOffset.UtcNow.AddHours(2);

        await f.Svc.StartMaintenanceAsync("n1", "patching", until, notifyNode: false, "admin");

        var node = await f.Db.Nodes.FindAsync("n1");
        node!.MaintenanceMode.Should().BeTrue();
        node.MaintenanceReason.Should().Be("patching");
        node.MaintenanceStartedAt.Should().NotBeNull();
        node.MaintenanceUntil.Should().Be(until);
        node.MaintenanceStartedBy.Should().Be("admin");
        node.LifecycleState.Should().Be(NodeLifecycleState.Active);   // orthogonal — never a state change
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeMaintenanceStarted);
        f.Published.OfType<NodeMaintenanceChangedEvent>().Should().ContainSingle(e => e.Enabled);
    }

    [Fact]
    public async Task StartMaintenance_Twice_AuditsExtended()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Active);

        await f.Svc.StartMaintenanceAsync("n1", "patching", null, false, "admin");
        await f.Svc.StartMaintenanceAsync("n1", "patching longer", DateTimeOffset.UtcNow.AddHours(4), false, "admin");

        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeMaintenanceStarted);
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeMaintenanceExtended);
    }

    [Fact]
    public async Task StartMaintenance_OnDisabled_Throws()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Disabled);

        var act = () => f.Svc.StartMaintenanceAsync("n1", "patching", null, false, "admin");

        await act.Should().ThrowAsync<InvalidLifecycleTransitionException>();
    }

    [Fact]
    public async Task EndMaintenance_ClearsColumns_PublishesEvent()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Active);
        await f.Svc.StartMaintenanceAsync("n1", "patching", null, false, "admin");

        await f.Svc.EndMaintenanceAsync("n1", "admin");

        var node = await f.Db.Nodes.FindAsync("n1");
        node!.MaintenanceMode.Should().BeFalse();
        node.MaintenanceReason.Should().BeNull();
        node.MaintenanceStartedAt.Should().BeNull();
        node.MaintenanceUntil.Should().BeNull();
        node.MaintenanceStartedBy.Should().BeNull();
        f.Published.OfType<NodeMaintenanceChangedEvent>().Should().ContainSingle(e => !e.Enabled);
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeMaintenanceEnded);
    }

    [Fact]
    public async Task EndMaintenance_WhenNotInMaintenance_NoOps()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Active);

        await f.Svc.EndMaintenanceAsync("n1", "admin");

        f.Published.Should().BeEmpty();
        f.Audits.Should().BeEmpty();
    }

    // ── Decommission ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Decommission_SetsGraceAndSnapshot_RevokesTrust()
    {
        var f = new Fixture();
        var node = await f.SeedNodeAsync("n1", NodeLifecycleState.Active, externalId: "ext-1");
        f.Db.NodeSecurities.Add(new SyncNodeSecurity
        {
            NodeId = "n1", CurrentTokenHash = "hash", CreatedTime = DateTime.UtcNow
        });
        f.Db.OutgoingBatches.AddRange(
            new SyncOutgoingBatch { NodeId = "n1", ChannelId = "c1", Status = 0 },
            new SyncOutgoingBatch { NodeId = "n1", ChannelId = "c1", Status = 1 },
            new SyncOutgoingBatch { NodeId = "n1", ChannelId = "c1", Status = 2 });   // closed — not open
        await f.Db.SaveChangesAsync();
        var raw = await f.BootstrapTokens.IssueAsync("n1", "admin");
        await f.Db.SaveChangesAsync();
        raw.Should().NotBeNullOrEmpty();

        await f.Svc.DecommissionAsync("n1", "hardware retired", gracePeriodMinutes: 90, "admin");

        var reloaded = await f.Db.Nodes.FindAsync("n1");
        reloaded!.LifecycleState.Should().Be(NodeLifecycleState.Decommissioning);
        reloaded.DecommissionReason.Should().Be("hardware retired");
        reloaded.DecommissionStartedAt.Should().NotBeNull();
        reloaded.DecommissionGraceUntil.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddMinutes(90), TimeSpan.FromMinutes(1));
        reloaded.DecommissionInitialOpenBatches.Should().Be(2);
        f.Db.NodeSecurities.Should().BeEmpty();                                     // trust revoked at drain start
        f.Db.NodeBootstrapTokens.Where(t => t.RevokedAt == null).Should().BeEmpty();
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeDecommissionStarted);
    }

    [Fact]
    public async Task ForceCompleteDecommission_TransitionsToDecommissioned_FreesExternalId()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Decommissioning, externalId: "ext-1");

        await f.Svc.ForceCompleteDecommissionAsync("n1", "admin");

        var node = await f.Db.Nodes.FindAsync("n1");
        node!.LifecycleState.Should().Be(NodeLifecycleState.Decommissioned);
        node.ExternalId.Should().BeEmpty();
        node.NodeName.Should().Contain("(decommissioned, was ext-1)");
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeDecommissionForced);
    }

    [Fact]
    public async Task FinalizeDecommission_SystemTrigger_AuditsCompleted()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Decommissioning, externalId: "ext-1");

        await f.Svc.FinalizeDecommissionAsync("n1", LifecycleTrigger.System, "drain complete");

        var node = await f.Db.Nodes.FindAsync("n1");
        node!.LifecycleState.Should().Be(NodeLifecycleState.Decommissioned);
        node.ExternalId.Should().BeEmpty();
        f.Audits.Should().ContainSingle(a => a.Action == NodeManagementAuditActions.NodeDecommissionCompleted);
        f.Published.OfType<NodeLifecycleChangedEvent>().Should().ContainSingle(e =>
            e.Trigger == LifecycleTrigger.System);
    }

    // ── History + event ordering invariants ────────────────────────────────────

    [Fact]
    public async Task History_IsAppendOnly_MigrationSeedPatternWritable()
    {
        var f = new Fixture();
        await f.SeedNodeAsync("n1", NodeLifecycleState.Active);

        await f.Svc.DisableAsync("n1", "r1", "admin");
        var firstRow = f.Db.NodeLifecycleHistories.AsNoTracking().Single();

        await f.Svc.EnableAsync("n1", "admin");

        var rows = f.Db.NodeLifecycleHistories.AsNoTracking().OrderBy(h => h.HistoryId).ToList();
        rows.Should().HaveCount(2);   // two commands → two rows, never mutated
        rows[0].Should().BeEquivalentTo(firstRow);
        rows[1].FromState.Should().Be(NodeLifecycleState.Disabled);
        rows[1].ToState.Should().Be(NodeLifecycleState.Active);

        // Migration-seed pattern: a row with Trigger=Migration and FromState=null is writable
        f.Db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId = "n1", FromState = null, ToState = NodeLifecycleState.Active,
            Trigger = LifecycleTrigger.Migration, Actor = "system", OccurredAt = DateTimeOffset.UtcNow,
        });
        await f.Db.SaveChangesAsync();
        f.Db.NodeLifecycleHistories.Count().Should().Be(3);
    }

    [Fact]
    public async Task Event_PublishedOnlyAfterCommit()
    {
        var transactionOpenAtPublish = new List<bool>();
        var f = new Fixture(configureMediator: (mediatorMock, db) =>
            mediatorMock
                .Setup(m => m.Publish(It.IsAny<NodeLifecycleChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback<NodeLifecycleChangedEvent, CancellationToken>((_, _) =>
                    transactionOpenAtPublish.Add(db.Database.CurrentTransaction is not null))
                .Returns(Task.CompletedTask));
        await f.SeedNodeAsync("n1", NodeLifecycleState.Disabled);

        await f.Svc.EnableAsync("n1", "admin");

        transactionOpenAtPublish.Should().ContainSingle().Which.Should().BeFalse();
    }
}
