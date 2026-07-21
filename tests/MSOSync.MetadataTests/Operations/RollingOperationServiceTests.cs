using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Rolling;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations;

public sealed class RollingOperationServiceTests : IDisposable
{
    private readonly AppDbContext              _db;
    private readonly Mock<IOperationService>   _opsMock;
    private readonly Mock<INodeLifecycleService> _lcMock;
    private readonly RollingOperationService   _sut;
    private readonly Guid                      _fixedOpId = Guid.NewGuid();

    public RollingOperationServiceTests()
    {
        _db      = TestDbContext.Create();
        _opsMock = new Mock<IOperationService>();
        _lcMock  = new Mock<INodeLifecycleService>();
        _opsMock.Setup(o => o.CreateAsync(
                It.IsAny<OperationType>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<OperationSource>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<OperationType, Guid?, Guid?, OperationSource, string, bool, bool, string, string?, CancellationToken>(
                async (type, refId, initBy, src, corrId, cc, cr, summary, meta, ct) =>
                {
                    _db.Operations.Add(new SyncOperation
                    {
                        OperationId = _fixedOpId, OperationType = type.ToString(),
                        Status = "Pending", Source = src.ToString(),
                        CorrelationId = corrId, StartedAt = DateTime.UtcNow,
                        MetadataJson = meta, Summary = summary,
                    });
                    await _db.SaveChangesAsync(ct);
                    return _fixedOpId;
                });
        _sut = new RollingOperationService(_db, _opsMock.Object, _lcMock.Object);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedNode(string nodeId, NodeLifecycleState state = NodeLifecycleState.Active,
        bool maintenance = false)
    {
        _db.Nodes.Add(new SyncNode
        {
            NodeId = nodeId, GroupId = "g", SyncUrl = "http://x",
            LifecycleState = state, MaintenanceMode = maintenance,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedRunningOp(Guid operationId, string nodeId, string stepStatus)
    {
        _db.Operations.Add(new SyncOperation
        {
            OperationId = operationId, OperationType = "RollingMaintenance",
            Status = "Running", Source = "User",
            CorrelationId = "c", StartedAt = DateTime.UtcNow,
        });
        _db.OperationSteps.Add(new SyncOperationStep
        {
            StepId = Guid.NewGuid(), OperationId = operationId,
            NodeId = nodeId, WaveNumber = 1, Status = stepStatus,
        });
        await _db.SaveChangesAsync();
    }

    private static RollingOperationPolicy DefaultPolicy(int waveSize = 2) =>
        new(WaveSize: waveSize, WavePercent: null, GateSoakSeconds: 10,
            WaveAction: "manual-confirm", WindowSeconds: null,
            TargetVersion: null, VerificationTimeoutSeconds: 300);

    [Fact]
    public async Task Create_assigns_waves_by_wave_size()
    {
        for (var i = 1; i <= 5; i++) await SeedNode($"n{i}");
        var nodeIds = new[] { "n1", "n2", "n3", "n4", "n5" };

        var id = await _sut.CreateAsync(OperationType.RollingMaintenance, nodeIds,
            DefaultPolicy(waveSize: 2), null, "admin");

        id.Should().Be(_fixedOpId);
        _opsMock.Verify(o => o.CreateAsync(
            OperationType.RollingMaintenance, null, null, OperationSource.User,
            It.IsAny<string>(), true, false,
            It.Is<string>(s => s.Contains("5 node")),
            It.Is<string?>(j => j != null && j.Contains("waveSize")),
            It.IsAny<CancellationToken>()), Times.Once);

        var steps = await _db.OperationSteps.Where(s => s.OperationId == _fixedOpId).ToListAsync();
        steps.Should().HaveCount(5);
        steps.All(s => s.Status == nameof(RollingStepStatus.Pending)).Should().BeTrue();
        steps.Select(s => s.WaveNumber).Distinct().Should().BeEquivalentTo([1, 2, 3]);
        steps.Count(s => s.WaveNumber == 1).Should().Be(2);
        steps.Count(s => s.WaveNumber == 2).Should().Be(2);
        steps.Count(s => s.WaveNumber == 3).Should().Be(1);
    }

    [Fact]
    public async Task Create_throws_when_node_not_active()
    {
        await SeedNode("bad-node", NodeLifecycleState.Disabled);

        var act = () => _sut.CreateAsync(OperationType.RollingMaintenance, ["bad-node"],
            DefaultPolicy(), null, "admin");

        await act.Should().ThrowAsync<OperationStateException>();
        _db.OperationSteps.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_throws_when_node_in_other_running_rolling_op()
    {
        await SeedNode("busy-node");
        var existingOpId = Guid.NewGuid();
        await SeedRunningOp(existingOpId, "busy-node", nameof(RollingStepStatus.Draining));

        var act = () => _sut.CreateAsync(OperationType.RollingMaintenance, ["busy-node"],
            DefaultPolicy(), null, "admin");

        await act.Should().ThrowAsync<OperationStateException>()
            .WithMessage("*already in a rolling operation*");
    }

    [Fact]
    public async Task Create_upgrade_requires_target_version()
    {
        await SeedNode("n1");
        var policy = DefaultPolicy() with { TargetVersion = null };

        var act = () => _sut.CreateAsync(OperationType.RollingUpgrade, ["n1"],
            policy, null, "admin");

        await act.Should().ThrowAsync<OperationStateException>()
            .WithMessage("*TargetVersion*");
    }

    [Fact]
    public async Task Abort_skips_pending_and_restores_inflight()
    {
        await SeedNode("n1"); await SeedNode("n2", NodeLifecycleState.Draining); await SeedNode("n3");
        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "RollingMaintenance",
            Status = "Running", Source = "User",
            CorrelationId = "c2", StartedAt = DateTime.UtcNow,
        });
        _db.OperationSteps.AddRange(
            new SyncOperationStep { StepId = Guid.NewGuid(), OperationId = opId, NodeId = "n1", WaveNumber = 1, Status = nameof(RollingStepStatus.Completed) },
            new SyncOperationStep { StepId = Guid.NewGuid(), OperationId = opId, NodeId = "n2", WaveNumber = 2, Status = nameof(RollingStepStatus.Draining) },
            new SyncOperationStep { StepId = Guid.NewGuid(), OperationId = opId, NodeId = "n3", WaveNumber = 3, Status = nameof(RollingStepStatus.Pending) });
        await _db.SaveChangesAsync();

        await _sut.AbortAsync(opId, "admin");

        var steps = await _db.OperationSteps.Where(s => s.OperationId == opId).ToListAsync();
        steps.Single(s => s.NodeId == "n3").Status.Should().Be(nameof(RollingStepStatus.Skipped));
        steps.Single(s => s.NodeId == "n2").Status.Should().Be(nameof(RollingStepStatus.Skipped));
        steps.Single(s => s.NodeId == "n1").Status.Should().Be(nameof(RollingStepStatus.Completed));

        _lcMock.Verify(l => l.ResumeFromDrainAsync("n2", It.IsAny<string?>(), "admin", It.IsAny<CancellationToken>()), Times.Once);
        _lcMock.Verify(l => l.ResumeFromDrainAsync("n3", It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var op = await _db.Operations.FindAsync(opId);
        op!.Status.Should().Be(nameof(OperationStatus.Cancelled));
    }

    [Fact]
    public async Task Pause_only_from_running()
    {
        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "RollingMaintenance",
            Status = "Pending", Source = "User",
            CorrelationId = "c3", StartedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var act = () => _sut.PauseAsync(opId);
        await act.Should().ThrowAsync<OperationStateException>();
    }

    [Fact]
    public async Task ConfirmStep_only_when_in_maintenance()
    {
        var stepId = Guid.NewGuid();
        var opId   = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "RollingMaintenance",
            Status = "Running", Source = "User",
            CorrelationId = "c4", StartedAt = DateTime.UtcNow,
        });
        _db.OperationSteps.Add(new SyncOperationStep
        {
            StepId = stepId, OperationId = opId, NodeId = "n1",
            WaveNumber = 1, Status = nameof(RollingStepStatus.Pending),
        });
        await _db.SaveChangesAsync();

        var act = () => _sut.ConfirmStepAsync(stepId);
        await act.Should().ThrowAsync<OperationStateException>();
    }
}
