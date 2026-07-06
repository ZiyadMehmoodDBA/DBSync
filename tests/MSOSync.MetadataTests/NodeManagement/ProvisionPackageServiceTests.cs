using System.IO.Compression;
using FluentAssertions;
using MSOSync.Metadata.Audit;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.NodeManagement;

public sealed class ProvisionPackageServiceTests : IDisposable
{
    private readonly MSOSync.Persistence.AppDbContext _db;
    private readonly AuditService                     _auditSvc;
    private readonly ProvisionPackageService          _sut;

    public ProvisionPackageServiceTests()
    {
        _db       = TestDbContext.Create();
        _auditSvc = new AuditService(_db);
        _sut      = new ProvisionPackageService(_db, _auditSvc);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task SeedNodeAsync(string nodeId = "node-1")
    {
        _db.Nodes.Add(new SyncNode
        {
            NodeId     = nodeId,
            GroupId    = "g1",
            SyncUrl    = "http://n1",
            Status     = "PROVISIONED",
            NodeType   = "target",
            ExternalId = "ext-node-1",
            NodeName   = "node-display-name",
            DbServer   = "srv",
            DbName     = "db",
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task StreamPackageAsync_ZipContainsExactlyFiveFiles()
    {
        await SeedNodeAsync();
        var ms = new MemoryStream();
        await _sut.StreamPackageAsync("node-1", "tok-abc", "test-actor", ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        zip.Entries.Should().HaveCount(5);
    }

    [Fact]
    public async Task StreamPackageAsync_ZipContainsAllExpectedFiles()
    {
        await SeedNodeAsync();
        var ms = new MemoryStream();
        await _sut.StreamPackageAsync("node-1", "tok-abc", "test-actor", ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.Name).ToList();
        names.Should().Contain("msosync-node.json");
        names.Should().Contain(".env.example");
        names.Should().Contain("README.md");
        names.Should().Contain("manifest.json");
        names.Should().Contain("checksums.txt");
    }

    [Fact]
    public async Task StreamPackageAsync_ChecksumsContainsAllFiles()
    {
        await SeedNodeAsync();
        var ms = new MemoryStream();
        await _sut.StreamPackageAsync("node-1", "tok-abc", "test-actor", ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var checkEntry = zip.GetEntry("checksums.txt")!;
        using var reader = new StreamReader(checkEntry.Open());
        var checksums = await reader.ReadToEndAsync();

        checksums.Should().Contain("msosync-node.json");
        checksums.Should().Contain(".env.example");
        checksums.Should().Contain("README.md");
        checksums.Should().Contain("manifest.json");
    }

    [Fact]
    public async Task StreamPackageAsync_NodeConfigContainsNodeId()
    {
        await SeedNodeAsync();
        var ms = new MemoryStream();
        await _sut.StreamPackageAsync("node-1", "tok-abc", "test-actor", ms);
        ms.Position = 0;

        using var zip    = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry        = zip.GetEntry("msosync-node.json")!;
        using var reader = new StreamReader(entry.Open());
        var json         = await reader.ReadToEndAsync();

        json.Should().Contain("\"nodeId\"");
        json.Should().Contain("node-1");
    }

    [Fact]
    public async Task StreamPackageAsync_WritesProvisionPackageDownloadedAudit()
    {
        await SeedNodeAsync("audit-node");
        var ms = new MemoryStream();
        await _sut.StreamPackageAsync("audit-node", "tok-secret", "actor-user", ms);

        var audit = _db.Audits
            .Where(a => a.ActionName == NodeManagementAuditActions.ProvisionPackageDownloaded)
            .SingleOrDefault();

        audit.Should().NotBeNull();
        audit!.ActionName.Should().Be("PROVISION_PACKAGE_DOWNLOADED");
        audit.ObjectName.Should().Contain("audit-node");
        audit.ObjectName.Should().NotContain("tok-secret");
        audit.Username.Should().Be("actor-user");
    }
}
