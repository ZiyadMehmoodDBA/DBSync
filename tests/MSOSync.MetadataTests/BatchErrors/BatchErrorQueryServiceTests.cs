using FluentAssertions;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.BatchErrors;

public sealed class BatchErrorQueryServiceTests
{
    private static (BatchErrorQueryService Svc, AppDbContext Db) Make()
    {
        var db         = TestDbContext.Create();
        var classifier = new ErrorSeverityClassifier();
        var svc        = new BatchErrorQueryService(db, classifier);
        return (svc, db);
    }

    /// <summary>Creates a minimal SyncOutgoingBatch to satisfy the FK on SyncBatchError.</summary>
    private static SyncOutgoingBatch MakeOutgoingBatch(long batchId) => new()
    {
        BatchId       = batchId,
        BatchSequence = batchId,
        NodeId        = "node-test",
        ChannelId     = "ch-test",
        Status        = 0
    };

    private static SyncBatchError MakeError(long batchId, string? conflictType,
        long? eventId = null) => new()
    {
        BatchId      = batchId,
        EventId      = eventId,
        ConflictType = conflictType,
        ErrorMessage = "test error",
        RetryCount   = 0,
        CreateTime   = DateTime.UtcNow
    };

    [Fact]
    public async Task GetBatchErrorsAsync_NoFilter_ReturnsAll()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.AddRange(MakeOutgoingBatch(1L), MakeOutgoingBatch(2L));
        await db.SaveChangesAsync();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),
            MakeError(1L, "Timeout"),
            MakeError(2L, "MetadataMissing"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(new BatchErrorFilter(), default);

        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetBatchErrorsAsync_FilterByBatchId_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.AddRange(MakeOutgoingBatch(1L), MakeOutgoingBatch(2L));
        await db.SaveChangesAsync();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),
            MakeError(2L, "Timeout"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(new BatchErrorFilter { BatchId = 1L }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().BatchId.Should().Be(1L);
    }

    [Fact]
    public async Task GetBatchErrorsAsync_FilterBySeverity_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.Add(MakeOutgoingBatch(1L));
        await db.SaveChangesAsync();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),   // Info
            MakeError(1L, "Timeout"),         // Warning
            MakeError(1L, "MetadataMissing"), // Critical
            MakeError(1L, null));             // Critical (null → Critical)
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(
            new BatchErrorFilter { Severity = ErrorSeverity.Warning }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().Severity.Should().Be("Warning");
    }

    [Fact]
    public async Task GetBatchErrorsAsync_FilterByConflictType_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.Add(MakeOutgoingBatch(1L));
        await db.SaveChangesAsync();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),
            MakeError(1L, "Timeout"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(
            new BatchErrorFilter { ConflictType = "Timeout" }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().ConflictType.Should().Be("Timeout");
    }

    [Fact]
    public async Task GetBatchErrorsAsync_SeverityDerivedFromConflictType()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.Add(MakeOutgoingBatch(1L));
        await db.SaveChangesAsync();
        db.BatchErrors.Add(MakeError(1L, "DuplicateKey"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(new BatchErrorFilter(), default);

        result.Items.Single().Severity.Should().Be("Info");
    }

    [Fact]
    public async Task GetBatchErrorsAsync_NullConflictType_SeverityIsCritical()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.Add(MakeOutgoingBatch(1L));
        await db.SaveChangesAsync();
        db.BatchErrors.Add(MakeError(1L, null));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(new BatchErrorFilter(), default);

        result.Items.Single().Severity.Should().Be("Critical");
    }

    [Fact]
    public async Task GetBatchErrorsAsync_Pagination_HonorsPageSize()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.Add(MakeOutgoingBatch(1L));
        await db.SaveChangesAsync();
        for (int i = 0; i < 7; i++)
            db.BatchErrors.Add(MakeError(1L, "Timeout"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(
            new BatchErrorFilter { Page = 2, PageSize = 3 }, default);

        result.TotalCount.Should().Be(7);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetBatchErrorByIdAsync_Exists_ReturnsDetailDto()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.Add(MakeOutgoingBatch(1L));
        await db.SaveChangesAsync();
        db.BatchErrors.Add(MakeError(1L, "Timeout", eventId: 42L));
        await db.SaveChangesAsync();

        var error = db.BatchErrors.Single();
        var dto   = await svc.GetBatchErrorByIdAsync(error.ErrorId, default);

        dto.Should().NotBeNull();
        dto!.ConflictType.Should().Be("Timeout");
        dto.EventId.Should().Be(42L);
        dto.Severity.Should().Be("Warning");
    }

    [Fact]
    public async Task GetBatchErrorByIdAsync_Missing_ReturnsNull()
    {
        var (svc, db) = Make();
        var dto = await svc.GetBatchErrorByIdAsync(99999L, default);
        dto.Should().BeNull();
    }

    [Fact]
    public async Task GetBatchErrorSummaryAsync_CountsBySeverity()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.Add(MakeOutgoingBatch(1L));
        await db.SaveChangesAsync();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),   // Info
            MakeError(1L, "Timeout"),         // Warning
            MakeError(1L, "Deadlock"),        // Warning
            MakeError(1L, "MetadataMissing"), // Critical
            MakeError(1L, null));             // Critical
        await db.SaveChangesAsync();

        var dto = await svc.GetBatchErrorSummaryAsync(null, null, null, default);

        dto.Info.Should().Be(1);
        dto.Warning.Should().Be(2);
        dto.Critical.Should().Be(2);
        dto.Total.Should().Be(dto.Info + dto.Warning + dto.Critical);
    }

    [Fact]
    public async Task GetBatchErrorSummaryAsync_FilterByBatchId_ScopesCounts()
    {
        var (svc, db) = Make();
        db.OutgoingBatches.AddRange(MakeOutgoingBatch(1L), MakeOutgoingBatch(2L));
        await db.SaveChangesAsync();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),  // batch 1, Info
            MakeError(2L, "Timeout"));       // batch 2, Warning
        await db.SaveChangesAsync();

        var dto = await svc.GetBatchErrorSummaryAsync(1L, null, null, default);

        dto.Info.Should().Be(1);
        dto.Warning.Should().Be(0);
        dto.Total.Should().Be(1);
    }
}
