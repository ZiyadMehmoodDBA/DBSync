using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Xunit;

namespace MSOSync.AppTests.Audit;

/// <summary>In-memory IPlatformRepository&lt;T&gt; for AppTests unit tests.</summary>
internal sealed class TestPlatformRepository<T>(AppDbContext db) : IPlatformRepository<T>
    where T : class
{
    public IQueryable<T> QueryAll() => db.Set<T>().AsNoTracking();
}


public sealed class CorrelationTimelineAssemblerTests : IDisposable
{
    private readonly AppDbContext _db;

    public CorrelationTimelineAssemblerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    private static SyncAudit MakeAudit(
        string correlationId,
        string action,
        DateTime? at = null,
        string? objectName = null,
        string? username = null)
        => new()
        {
            CorrelationId = correlationId,
            ActionName    = action,
            ObjectName    = objectName ?? $"Description of {action}",
            Username      = username,
            CreateTime    = at ?? DateTime.UtcNow,
        };

    // Test 1: Unknown correlationId (no rows) => AssembleAsync returns null
    [Fact]
    public async Task AssembleAsync_UnknownCorrelationId_ReturnsNull()
    {
        var assembler = new CorrelationTimelineAssembler(_db, new TestPlatformRepository<SyncAudit>(_db));
        var result = await assembler.AssembleAsync("no-such-correlation-id", CancellationToken.None);
        Assert.Null(result);
    }

    // Test 2: Events spanning 3 phases => phases grouped correctly
    [Fact]
    public async Task AssembleAsync_EventsInThreePhases_PhasesGroupedCorrectly()
    {
        const string corrId = "corr-phases-test";
        var now = DateTime.UtcNow;

        _db.Audits.AddRange(
            MakeAudit(corrId, "NODE_REGISTERED",       at: now.AddSeconds(-3)),
            MakeAudit(corrId, "NODE_APPROVED",         at: now.AddSeconds(-2)),
            MakeAudit(corrId, "CONFIGURATION_APPLIED", at: now.AddSeconds(-1)));
        await _db.SaveChangesAsync();

        var assembler = new CorrelationTimelineAssembler(_db, new TestPlatformRepository<SyncAudit>(_db));
        var result = await assembler.AssembleAsync(corrId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result!.Phases, p => p.PhaseName == "Registration");
        Assert.Contains(result.Phases,  p => p.PhaseName == "Lifecycle");
        Assert.Contains(result.Phases,  p => p.PhaseName == "Configuration");
    }

    // Test 3: Any event with FAILED in action name => IsFailedWorkflow = true
    [Fact]
    public async Task AssembleAsync_EventWithFailedAction_IsFailedWorkflowTrue()
    {
        const string corrId = "corr-failed-test";
        var now = DateTime.UtcNow;

        _db.Audits.AddRange(
            MakeAudit(corrId, "NODE_ACTIVATED",         at: now.AddSeconds(-1)),
            MakeAudit(corrId, "NODE_ACTIVATION_FAILED", at: now));
        await _db.SaveChangesAsync();

        var assembler = new CorrelationTimelineAssembler(_db, new TestPlatformRepository<SyncAudit>(_db));
        var result = await assembler.AssembleAsync(corrId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsFailedWorkflow);
        Assert.NotNull(result.FailureSummary);
    }

    // Test 4: EntityChips array is empty (SyncAudit has no EntityType/EntityId columns)
    [Fact]
    public async Task AssembleAsync_AuditEvents_EntityChipsIsEmpty()
    {
        const string corrId = "corr-chips-test";
        var now = DateTime.UtcNow;

        _db.Audits.AddRange(
            MakeAudit(corrId, "NODE_ACTIVATED",         at: now.AddSeconds(-2)),
            MakeAudit(corrId, "CONFIGURATION_APPLIED",  at: now.AddSeconds(-1)));
        await _db.SaveChangesAsync();

        var assembler = new CorrelationTimelineAssembler(_db, new TestPlatformRepository<SyncAudit>(_db));
        var result = await assembler.AssembleAsync(corrId, CancellationToken.None);

        Assert.NotNull(result);
        // SyncAudit has no EntityType/EntityId fields; chips are always empty
        Assert.Empty(result!.EntityChips);
    }

    // Test 5: DurationSincePrevious is correct: null for first event, ~4s for second
    [Fact]
    public async Task AssembleAsync_TwoEvents_DurationSincePreviousIsCorrect()
    {
        const string corrId = "corr-duration-test";
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddSeconds(4);

        _db.Audits.AddRange(
            MakeAudit(corrId, "NODE_ACTIVATED",         at: t1),
            MakeAudit(corrId, "CONFIGURATION_APPLIED",  at: t2));
        await _db.SaveChangesAsync();

        var assembler = new CorrelationTimelineAssembler(_db, new TestPlatformRepository<SyncAudit>(_db));
        var result = await assembler.AssembleAsync(corrId, CancellationToken.None);

        Assert.NotNull(result);
        var allEvents = result!.Phases
            .SelectMany(p => p.Events)
            .OrderBy(e => e.OccurredAt)
            .ToArray();

        Assert.Equal(2, allEvents.Length);
        Assert.Null(allEvents[0].DurationSincePrevious);
        Assert.NotNull(allEvents[1].DurationSincePrevious);
        Assert.Equal(TimeSpan.FromSeconds(4), allEvents[1].DurationSincePrevious!.Value);
    }

    public void Dispose() => _db.Dispose();
}
