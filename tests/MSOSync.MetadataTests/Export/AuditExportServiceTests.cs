using FluentAssertions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.Export;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Export;

public sealed class AuditExportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuditExportService _sut;

    public AuditExportServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new AuditExportService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SyncAudit Audit(long id, string? username = "alice",
        string? actionName = "LOGIN_SUCCESS", DateTime? createTime = null) => new()
    {
        AuditId    = id,
        Username   = username,
        ActionName = actionName,
        ObjectName = null,
        CreateTime = createTime ?? DateTime.UtcNow,
    };

    [Fact]
    public async Task ExportCsv_WritesHeaderAndDataRows()
    {
        _db.Audits.AddRange(Audit(1), Audit(2));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new AuditFilter(), default);

        count.Should().Be(2);
        ms.Position = 0;
        var lines = new StreamReader(ms).ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("auditId,");
        lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExportCsv_FilterByUsername_ReturnsMatchingCount()
    {
        _db.Audits.AddRange(Audit(1, username: "alice"), Audit(2, username: "bob"));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new AuditFilter { Username = "alice" }, default);

        count.Should().Be(1);
    }

    [Fact]
    public async Task ExportJson_ProducesValidJsonArray()
    {
        _db.Audits.Add(Audit(1));
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportJsonAsync(ms, new AuditFilter(), default);

        count.Should().Be(1);
        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd().Trim();
        json.Should().StartWith("[").And.EndWith("]");
    }

    [Fact]
    public async Task ExportCsv_NullCreateTime_ExcludesRow()
    {
        _db.Audits.AddRange(
            new SyncAudit { AuditId = 1, Username = "alice", CreateTime = DateTime.UtcNow },
            new SyncAudit { AuditId = 2, Username = "bob",   CreateTime = null });
        await _db.SaveChangesAsync();
        using var ms = new MemoryStream();

        var count = await _sut.ExportCsvAsync(ms, new AuditFilter(), default);

        count.Should().Be(1);
    }
}
