using FluentAssertions;
using Moq;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Operations;
using MSOSync.Persistence.Entities;
using MSOSync.Security;
using Xunit;

namespace MSOSync.MetadataTests.NodeManagement;

public sealed class NodeLifecycleServiceTests
{
    private static NodeLifecycleService MakeService(out MSOSync.Persistence.AppDbContext db)
    {
        db = TestDbContext.Create();
        var diffSvc       = new RegistrationDiffService();
        var auditSvc      = new Mock<IAuditService>();
        var mediator      = new Mock<IMediator>();
        var options       = Options.Create(new LifecycleOptions());
        var hasher        = new BCryptPasswordHasher();
        var operationSvc  = new Mock<IOperationService>();
        operationSvc.Setup(o => o.CreateAsync(
            It.IsAny<OperationType>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<OperationSource>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return new NodeLifecycleService(
            db,
            diffSvc,
            auditSvc.Object,
            mediator.Object,
            new NodeLifecycleStateMachine(),
            new NodeLifecycleHistoryService(db),
            new BootstrapTokenService(db, hasher, options),
            new NodeSecurityService(db, hasher),
            new NodeLifecycleLockRegistry(),
            options,
            new ConfigurationBuilder().Build(),
            NullLogger<NodeLifecycleService>.Instance,
            operationSvc.Object);
    }

    [Fact]
    public async Task RegisterAsync_NewNode_ReturnsPendingRegistration()
    {
        var svc = MakeService(out var db);

        var id = await svc.RegisterAsync(new InboundRegistrationDto(
            "ext-1", "Node1", "target", null));

        id.Should().BeGreaterThan(0);
        var req = db.RegistrationRequests.Find(id);
        req!.Status.Should().Be(RegistrationStatus.Pending);
        req.RegistrationType.Should().Be(RegistrationType.New);
    }

    [Fact]
    public async Task RegisterAsync_ExistingRegisteredNode_SetsReRegistration()
    {
        var svc = MakeService(out var db);

        db.Nodes.Add(new SyncNode
        {
            NodeId  = "ext-2", GroupId = "g1", SyncUrl = "http://n",
            ExternalId = "ext-2", LifecycleState = NodeLifecycleState.Active
        });
        await db.SaveChangesAsync();

        var id = await svc.RegisterAsync(new InboundRegistrationDto(
            "ext-2", "Node2", "target", null));

        var req = db.RegistrationRequests.Find(id)!;
        req.RegistrationType.Should().Be(RegistrationType.ReRegistration);
    }

    [Fact]
    public async Task ApproveAsync_PendingRegistration_SetsApproved()
    {
        var svc = MakeService(out var db);

        var id = await svc.RegisterAsync(new InboundRegistrationDto(
            "ext-3", "Node3", "target", null));

        await svc.ApproveAsync(id, "looks good", "admin");

        var req = db.RegistrationRequests.Find(id)!;
        req.Status.Should().Be(RegistrationStatus.Approved);
        req.ProcessedBy.Should().Be("admin");
        req.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveAsync_AlreadyApproved_Throws()
    {
        var svc = MakeService(out var db);

        var id = await svc.RegisterAsync(new InboundRegistrationDto(
            "ext-4", "Node4", "target", null));
        await svc.ApproveAsync(id, null, "admin");

        var act = () => svc.ApproveAsync(id, null, "admin");

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task RejectAsync_PendingRegistration_SetsRejected()
    {
        var svc = MakeService(out var db);

        var id = await svc.RegisterAsync(new InboundRegistrationDto(
            "ext-5", "Node5", "target", null));

        await svc.RejectAsync(id, "not valid", "admin");

        var req = db.RegistrationRequests.Find(id)!;
        req.Status.Should().Be(RegistrationStatus.Rejected);
    }

    [Fact]
    public async Task RejectAsync_AlreadyRejected_ThrowsConcurrencyException()
    {
        var svc = MakeService(out var db);

        var id = await svc.RegisterAsync(new InboundRegistrationDto(
            "ext-5a", "Node5a", "target", null));
        await svc.RejectAsync(id, "not valid", "admin");

        var act = () => svc.RejectAsync(id, "not valid", "admin");

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task BulkApproveAsync_MixedIds_ReturnsCorrectStatuses()
    {
        var svc = MakeService(out var db);

        var id1 = await svc.RegisterAsync(new InboundRegistrationDto(
            "ext-6", "Node6", "target", null));
        var id2 = await svc.RegisterAsync(new InboundRegistrationDto(
            "ext-7", "Node7", "target", null));
        await svc.ApproveAsync(id2, null, "admin");  // already approved

        var results = await svc.BulkApproveAsync([id1, id2, 99999L], "admin");

        results.Should().HaveCount(3);
        results.First(r => r.Id == id1).Status.Should().Be("Approved");
        results.First(r => r.Id == id2).Status.Should().Be("AlreadyApproved");
        results.First(r => r.Id == 99999L).Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task ProvisionAsync_ValidRequest_CreatesNodeAndReturnsToken()
    {
        var svc = MakeService(out var db);

        var result = await svc.ProvisionAsync(new ProvisionRequestDto(
            "NewNode", "ext-8", "target", "db-server", "db-name", "g1", null),
            "admin");

        result.NodeId.Should().Be("ext-8");
        result.Token.Should().NotBeNullOrWhiteSpace();
        result.Token.Length.Should().BeGreaterThan(20);

        var node = db.Nodes.Find("ext-8")!;
        node.LifecycleState.Should().Be(NodeLifecycleState.PendingRegistration);
    }
}
