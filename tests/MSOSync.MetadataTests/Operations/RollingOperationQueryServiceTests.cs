using FluentAssertions;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Rolling;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations;

public sealed class RollingOperationQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RollingOperationQueryService _sut;

    public RollingOperationQueryServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new RollingOperationQueryService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static readonly RollingOperationPolicy Policy = new(
        WaveSize: 2, WavePercent: null, GateSoakSeconds: 10,
        WaveAction: "manual-confirm", WindowSeconds: null,
        TargetVersion: null, VerificationTimeoutSeconds: 300);

    private async Task<Guid> SeedOperationWithSteps()
    {
        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "RollingMaintenance",
            Status = "Running", Source = "User",
            CorrelationId = "c1", StartedAt = DateTime.UtcNow,
            MetadataJson = RollingOperationPolicy.ToJson(Policy),
        });
        _db.OperationSteps.AddRange(
            new SyncOperationStep { StepId = Guid.NewGuid(), OperationId = opId, NodeId = "node-c", WaveNumber = 2, Status = "Pending" },
            new SyncOperationStep { StepId = Guid.NewGuid(), OperationId = opId, NodeId = "node-a", WaveNumber = 1, Status = "Completed" },
            new SyncOperationStep { StepId = Guid.NewGuid(), OperationId = opId, NodeId = "node-b", WaveNumber = 1, Status = "Completed" });
        await _db.SaveChangesAsync();
        return opId;
    }

    [Fact]
    public async Task GetDetail_returns_operation_with_ordered_steps()
    {
        var opId = await SeedOperationWithSteps();

        var result = await _sut.GetDetailAsync(opId);

        result.OperationId.Should().Be(opId);
        result.OperationType.Should().Be("RollingMaintenance");
        result.Status.Should().Be("Running");
        result.Policy.WaveSize.Should().Be(2);
        result.Policy.WaveAction.Should().Be("manual-confirm");
        result.Steps.Should().HaveCount(3);
        result.Steps[0].NodeId.Should().Be("node-a");
        result.Steps[1].NodeId.Should().Be("node-b");
        result.Steps[2].NodeId.Should().Be("node-c");
        result.Steps[2].WaveNumber.Should().Be(2);
    }

    [Fact]
    public async Task GetDetail_unknown_id_throws_not_found()
    {
        var act = () => _sut.GetDetailAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
