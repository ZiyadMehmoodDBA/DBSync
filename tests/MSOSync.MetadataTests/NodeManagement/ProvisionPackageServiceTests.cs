using System.IO.Compression;
using FluentAssertions;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.NodeManagement;

public sealed class ProvisionPackageServiceTests
{
    private readonly ProvisionPackageService _sut = new();

    private static SyncNode MakeNode() =>
        new() { NodeId = "node-1", GroupId = "g1", SyncUrl = "http://n1", Status = "PROVISIONED",
                DbServer = "srv", DbName = "db" };

    private static ProvisionResultDto MakeProvision() =>
        new("node-1", "tok-abc");

    [Fact]
    public async Task WriteAsync_ZipContainsExactlyFiveFiles()
    {
        var ms = new MemoryStream();
        await _sut.WriteAsync(MakeProvision(), MakeNode(), ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        zip.Entries.Should().HaveCount(5);
    }

    [Fact]
    public async Task WriteAsync_ZipContainsAllExpectedFiles()
    {
        var ms = new MemoryStream();
        await _sut.WriteAsync(MakeProvision(), MakeNode(), ms);
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
    public async Task WriteAsync_ChecksumsContainsAllFiles()
    {
        var ms = new MemoryStream();
        await _sut.WriteAsync(MakeProvision(), MakeNode(), ms);
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
    public async Task WriteAsync_NodeConfigContainsNodeId()
    {
        var ms = new MemoryStream();
        await _sut.WriteAsync(MakeProvision(), MakeNode(), ms);
        ms.Position = 0;

        using var zip    = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry        = zip.GetEntry("msosync-node.json")!;
        using var reader = new StreamReader(entry.Open());
        var json         = await reader.ReadToEndAsync();

        json.Should().Contain("\"nodeId\"");
        json.Should().Contain("node-1");
    }
}
