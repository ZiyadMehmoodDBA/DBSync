using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Rolling;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Scheduler.Workers;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class RollingOperationWorkerTests
{
    private readonly Mock<INodeLifecycleService>         _lifecycle = new();
    private readonly Mock<INodeLifecycleHistoryService>  _history   = new();
    private readonly Mock<IWorkerStatusRegistry>         _registry  = new();
    private readonly Mock<IClock>                        _clock     = new();
    private readonly DateTime                            _now       = new(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc);
    private readonly DbContextOptions<AppDbContext>      _dbOpts;

    public RollingOperationWorkerTests()
    {
        _dbOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _clock.Setup(c => c.UtcNow).Returns(_now);
    }

    private AppDbContext NewDb() => new(_dbOpts);

    private RollingOperationWorker BuildWorker()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewDb());
        services.AddScoped(_ => _lifecycle.Object);
        services.AddScoped(_ => _history.Object);
        services.AddScoped(_ => _clock.Object);

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new RollingOperationWorker(
            scopeFactory,
            Options.Create(new LifecycleOptions { RollingWorkerIntervalSeconds = 15 }),
            NullLogger<RollingOperationWorker>.Instance,
            _registry.Object);
    }

    private async Task SeedNode(string nodeId, NodeLifecycleState state = NodeLifecycleState.Active,
        DateTimeOffset? drainCompletedAt = null, bool maintenance = false,
        ConnectivityStatus connectivity = ConnectivityStatus.Reachable,
        string? agentVersion = null)
    {
        using var db = NewDb();
        db.Nodes.Add(new SyncNode
        {
            NodeId = nodeId, GroupId = "g", SyncUrl = "http://x",
            LifecycleState = state, DrainCompletedAt = drainCompletedAt,
            MaintenanceMode = maintenance, ConnectivityStatus = connectivity,
            AgentVersion = agentVersion,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedBatch(string nodeId, byte status = 0)
    {
        using var db = NewDb();
        db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            BatchId = new Random().Next(1, int.MaxValue),
            NodeId = nodeId, ChannelId = "ch", Status = status,
            BatchSequence = 1, RowCount = 1,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedOperation(string type = "RollingMaintenance",
        string status = "Running", string? targetVersion = null)
    {
        var policy = new RollingOperationPolicy(
            WaveSize: 1, WavePercent: null, GateSoakSeconds: 0,
            WaveAction: "manual-confirm", WindowSeconds: null,
            TargetVersion: targetVersion, VerificationTimeoutSeconds: 60);
        using var db = NewDb();
        var opId = Guid.NewGuid();
        db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = type, Status = status,
            Source = "User", CorrelationId = "c", StartedAt = DateTime.UtcNow,
            MetadataJson = RollingOperationPolicy.ToJson(policy),
        });
        await db.SaveChangesAsync();
        return opId;
    }

    private async Task SeedStep(Guid opId, string nodeId, int wave, string status,
        DateTime? startedAt = null)
    {
        using var db = NewDb();
        db.OperationSteps.Add(new SyncOperationStep
        {
            StepId = Guid.NewGuid(), OperationId = opId, NodeId = nodeId,
            WaveNumber = wave, Status = status, StartedAt = startedAt,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RunTick_marks_drain_completed_when_no_open_batches()
    {
        await SeedNode("n1", NodeLifecycleState.Draining);

        await BuildWorker().RunTickAsync(CancellationToken.None);

        using var db = NewDb();
        var node = await db.Nodes.FindAsync("n1");
        node!.DrainCompletedAt.Should().NotBeNull();
        _history.Verify(h => h.WriteTransitionAsync(
            It.Is<LifecycleTransitionRecord>(r =>
                r.NodeId == "n1" && r.Reason == NodeManagementAuditActions.NodeDrainCompleted),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunTick_does_not_mark_drain_completed_with_open_batches()
    {
        await SeedNode("n2", NodeLifecycleState.Draining);
        await SeedBatch("n2", status: 0);

        await BuildWorker().RunTickAsync(CancellationToken.None);

        using var db = NewDb();
        var node = await db.Nodes.FindAsync("n2");
        node!.DrainCompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task RunTick_starts_first_wave_pending_steps()
    {
        await SeedNode("n3");
        var opId = await SeedOperation();
        await SeedStep(opId, "n3", wave: 1, status: "Pending");

        await BuildWorker().RunTickAsync(CancellationToken.None);

        _lifecycle.Verify(l => l.StartDrainAsync("n3", It.IsAny<string?>(), "system",
            It.IsAny<CancellationToken>()), Times.Once);

        using var db = NewDb();
        var step = await db.OperationSteps.SingleAsync(s => s.NodeId == "n3");
        step.Status.Should().Be(nameof(RollingStepStatus.Draining));
        step.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunTick_moves_drained_step_to_maintenance()
    {
        var drainedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await SeedNode("n4", NodeLifecycleState.Draining, drainCompletedAt: drainedAt);
        var opId = await SeedOperation();
        await SeedStep(opId, "n4", wave: 1, status: "Draining", startedAt: _now.AddMinutes(-5));

        await BuildWorker().RunTickAsync(CancellationToken.None);

        _lifecycle.Verify(l => l.StartMaintenanceAsync("n4", It.IsAny<string>(),
            It.IsAny<DateTimeOffset?>(), false, "system", It.IsAny<CancellationToken>()), Times.Once);

        using var db = NewDb();
        var step = await db.OperationSteps.SingleAsync(s => s.NodeId == "n4");
        step.Status.Should().Be(nameof(RollingStepStatus.InMaintenance));
    }

    [Fact]
    public async Task RunTick_completes_maintenance_step_after_awaiting_verification()
    {
        await SeedNode("n5", NodeLifecycleState.Active, maintenance: false);
        var opId = await SeedOperation();
        await SeedStep(opId, "n5", wave: 1, status: "AwaitingVerification",
            startedAt: _now.AddMinutes(-10));

        await BuildWorker().RunTickAsync(CancellationToken.None);

        _lifecycle.Verify(l => l.EndMaintenanceAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        using var db = NewDb();
        var step = await db.OperationSteps.SingleAsync(s => s.NodeId == "n5");
        step.Status.Should().Be(nameof(RollingStepStatus.Completed));
    }

    [Fact]
    public async Task RunTick_upgrade_step_waits_for_target_version_then_completes()
    {
        await SeedNode("n6", agentVersion: "1.9");
        var opId = await SeedOperation(type: "RollingUpgrade", targetVersion: "2.0.0");
        await SeedStep(opId, "n6", wave: 1, status: "AwaitingVerification",
            startedAt: _now.AddSeconds(-10));

        var worker = BuildWorker();

        // First tick: version mismatch → stay AwaitingVerification
        await worker.RunTickAsync(CancellationToken.None);
        using (var db1 = NewDb())
        {
            var step1 = await db1.OperationSteps.SingleAsync(s => s.NodeId == "n6");
            step1.Status.Should().Be(nameof(RollingStepStatus.AwaitingVerification));
        }

        // Update AgentVersion to match
        using (var db2 = NewDb())
        {
            var node = await db2.Nodes.FindAsync("n6");
            node!.AgentVersion = "2.0.0";
            await db2.SaveChangesAsync();
        }

        // Second tick: version matches → Completed
        await worker.RunTickAsync(CancellationToken.None);
        using var db3 = NewDb();
        var step2 = await db3.OperationSteps.SingleAsync(s => s.NodeId == "n6");
        step2.Status.Should().Be(nameof(RollingStepStatus.Completed));
    }

    [Fact]
    public async Task RunTick_upgrade_verification_timeout_fails_step_and_pauses_op()
    {
        await SeedNode("n7", agentVersion: "1.9");
        var opId = await SeedOperation(type: "RollingUpgrade", targetVersion: "2.0.0");
        // StartedAt 2 minutes ago; VerificationTimeoutSeconds = 60 → timed out
        await SeedStep(opId, "n7", wave: 1, status: "AwaitingVerification",
            startedAt: _now.AddSeconds(-120));

        await BuildWorker().RunTickAsync(CancellationToken.None);

        using var db = NewDb();
        var step = await db.OperationSteps.SingleAsync(s => s.NodeId == "n7");
        step.Status.Should().Be(nameof(RollingStepStatus.Failed));
        step.ErrorMessage.Should().Contain("timeout");

        var op = await db.Operations.FindAsync(opId);
        op!.Status.Should().Be("Paused");
    }

    [Fact]
    public async Task RunTick_gate_failure_pauses_operation()
    {
        // Wave 1 node: completed but now Unreachable
        await SeedNode("n8a", connectivity: ConnectivityStatus.Unreachable);
        // Wave 2 node: pending
        await SeedNode("n8b");
        var opId = await SeedOperation();

        using (var db = NewDb())
        {
            db.OperationSteps.AddRange(
                new SyncOperationStep
                {
                    StepId = Guid.NewGuid(), OperationId = opId, NodeId = "n8a",
                    WaveNumber = 1, Status = "Completed", CompletedAt = _now.AddSeconds(-120)
                },
                new SyncOperationStep
                {
                    StepId = Guid.NewGuid(), OperationId = opId, NodeId = "n8b",
                    WaveNumber = 2, Status = "Pending"
                });
            await db.SaveChangesAsync();
        }

        await BuildWorker().RunTickAsync(CancellationToken.None);

        _lifecycle.Verify(l => l.StartDrainAsync("n8b", It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        using var verifyDb = NewDb();
        var op = await verifyDb.Operations.FindAsync(opId);
        op!.Status.Should().Be("Paused");
        op.ProgressMessage.Should().Contain("Health gate failed");
    }

    [Fact]
    public async Task RunTick_completes_operation_when_all_steps_terminal()
    {
        await SeedNode("n9");
        var opId = await SeedOperation();
        // Add completed step, but we'll set it via direct DB write
        using (var db = NewDb())
        {
            db.OperationSteps.Add(new SyncOperationStep
            {
                StepId = Guid.NewGuid(), OperationId = opId, NodeId = "n9",
                WaveNumber = 1, Status = "AwaitingVerification",
                StartedAt = _now.AddSeconds(-10),
            });
            await db.SaveChangesAsync();
        }

        await BuildWorker().RunTickAsync(CancellationToken.None);

        using var db2 = NewDb();
        var op = await db2.Operations.FindAsync(opId);
        op!.Status.Should().Be(nameof(OperationStatus.Completed));
        op.Result.Should().Be(nameof(OperationResult.Success));
        op.ProgressPercent.Should().Be(100);
    }
}
