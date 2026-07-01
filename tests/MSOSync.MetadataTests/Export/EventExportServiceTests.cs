using FluentAssertions;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Export;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Export;

public sealed class EventExportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly EventExportService _sut;

    public EventExportServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new EventExportService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SyncDataEvent Event(long id,
        string sourceNodeId = "N1", string tableName = "dbo.Items",
        DateTime? createTime = null) => new()
    {
        EventId      = id,
        TriggerId    = "T1",
        SourceNodeId = sourceNodeId,
        ChannelId    = "C1",
        EventType    = 'I',
        TableName    = tableName,
        CreateTime   = createTime ?? DateTime.UtcNow,
        IsProcessed  = false,
    };

    [Fact]
    public async Task ExportCsv_WritesHeaderAndDataRows()
    {
        _db.DataEvents.AddRange(Event(1), Event(2));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new EventFilter(), default);

        count.Should().Be(2);
        ms.Position = 0;
        var lines = new StreamReader(ms).ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("eventId,");
        lines.Should().HaveCount(3); // header + 2 rows
    }

    [Fact]
    public async Task ExportCsv_FilterApplied_ReturnsMatchingCount()
    {
        _db.DataEvents.AddRange(Event(1, sourceNodeId: "N1"), Event(2, sourceNodeId: "N2"));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new EventFilter { SourceNodeId = "N1" }, default);

        count.Should().Be(1);
    }

    [Fact]
    public async Task ExportCsv_FieldContainsComma_IsQuoted()
    {
        _db.DataEvents.Add(Event(1, tableName: "dbo.Cust,omers"));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        await _sut.ExportCsvAsync(ms, new EventFilter(), default);

        ms.Position = 0;
        new StreamReader(ms).ReadToEnd().Should().Contain("\"dbo.Cust,omers\"");
    }

    [Fact]
    public async Task ExportJson_ProducesValidJsonArray()
    {
        _db.DataEvents.Add(Event(1));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportJsonAsync(ms, new EventFilter(), default);

        count.Should().Be(1);
        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd().Trim();
        json.Should().StartWith("[").And.EndWith("]");
    }

    [Fact]
    public async Task ExportCsv_EmptyResult_WritesHeaderOnly()
    {
        using var ms = new MemoryStream();
        var count = await _sut.ExportCsvAsync(ms, new EventFilter(), default);
        count.Should().Be(0);
        ms.Position = 0;
        var lines = new StreamReader(ms).ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1); // header only
    }
}
