using FluentAssertions;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.NodeManagement;

public sealed class RegistrationDiffServiceTests
{
    private readonly RegistrationDiffService _sut = new();

    private static SyncNode MakeNode(string? dbServer = "srv", string? dbName = "db") =>
        new() { NodeId = "n1", GroupId = "g1", SyncUrl = "http://n1", LifecycleState = NodeLifecycleState.Active,
                DbServer = dbServer, DbName = dbName };

    private static RegistrationMetadataDto MakeMeta(
        string? hostname = "host1", string? agentVersion = "1.0") =>
        new(1,
            Machine: new MachineMetadata(hostname, "Win11", "machine1"),
            Database: null, Application: new ApplicationMetadata(agentVersion, ".NET 9", null),
            Hardware: null);

    [Fact]
    public void Compute_NewNode_AllFieldsAdded()
    {
        var node = MakeNode(dbServer: null, dbName: null);
        var meta = MakeMeta();

        var result = _sut.Compute(meta, node);

        result.Items.Should().Contain(i =>
            i.Field == "Machine.HostName" &&
            i.ChangeType == RegistrationChangeType.Added &&
            i.IncomingValue == "host1");
    }

    [Fact]
    public void Compute_SameValues_EmptyByDefault()
    {
        var node = MakeNode();
        var meta = new RegistrationMetadataDto(1, null, null, null, null);

        var result = _sut.Compute(meta, node);

        result.Items.Where(i => i.ChangeType != RegistrationChangeType.Unchanged)
            .Should().BeEmpty();
    }

    [Fact]
    public void Compute_IncludeUnchanged_ContainsUnchangedItems()
    {
        var node = MakeNode();
        var meta = new RegistrationMetadataDto(1, null, null, null, null);

        var result = _sut.Compute(meta, node, includeUnchanged: true);

        result.Items.Should().Contain(i => i.ChangeType == RegistrationChangeType.Unchanged);
    }

    [Fact]
    public void Compute_ModifiedDbInstanceName_ShowsModified()
    {
        // node.DbName = "old-db"; incoming reports Database.InstanceName = "new-db"
        var node = MakeNode(dbName: "old-db");
        var meta = new RegistrationMetadataDto(1, null,
            Database: new DatabaseMetadata(null, null, null, "new-db"),
            Application: null, Hardware: null);

        var result = _sut.Compute(meta, node);

        result.Items.Should().Contain(i =>
            i.Field == "Database.InstanceName" &&
            i.ChangeType == RegistrationChangeType.Modified &&
            i.IncomingValue == "new-db");
    }

    [Fact]
    public void Compute_RemovedField_ShowsRemoved()
    {
        var node = MakeNode();
        node.DbServer = "existing-server";
        var meta = new RegistrationMetadataDto(1,
            Machine: new MachineMetadata(null, null, null),
            Database: null, Application: null, Hardware: null);

        var result = _sut.Compute(meta, node);

        result.Items.Should().Contain(i =>
            i.ChangeType == RegistrationChangeType.Removed);
    }
}
