// tests/MSOSync.EngineTests/SqlServerTriggerBuilderTests.cs
using FluentAssertions;
using MSOSync.Persistence.Entities;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class SqlServerTriggerBuilderTests
{
    private static SyncTrigger MakeTrigger(bool insert = true, bool update = true, bool delete = true) =>
        new()
        {
            TriggerId    = "t-orders",
            SourceTable  = "dbo.Orders",
            ChannelId    = "default",
            SyncOnInsert = insert,
            SyncOnUpdate = update,
            SyncOnDelete = delete
        };

    private readonly SqlServerTriggerBuilder _builder = new();

    [Fact]
    public void BuildDdl_ContainsCreateOrAlterTrigger()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("CREATE OR ALTER TRIGGER");
    }

    [Fact]
    public void BuildDdl_ContainsTableName()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("[dbo].[Orders]");
    }

    [Fact]
    public void BuildDdl_ContainsForJsonPath()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("FOR JSON PATH");
    }

    [Fact]
    public void BuildDdl_ContainsCurrentTransactionId()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("CURRENT_TRANSACTION_ID()");
    }

    [Fact]
    public void BuildDdl_EmbedsNodeIdAsLiteral()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("N'hub'");
    }

    [Fact]
    public void BuildDdl_InsertOnlyFlag_AfterClauseOnlyHasInsert()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(insert: true, update: false, delete: false), "hub");
        ddl.Should().Contain("AFTER INSERT");
        ddl.Should().NotContain("UPDATE");
        ddl.Should().NotContain("DELETE");
    }

    [Fact]
    public void BuildDdl_AllFlags_AfterClauseHasAllThree()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("INSERT");
        ddl.Should().Contain("UPDATE");
        ddl.Should().Contain("DELETE");
    }

    [Fact]
    public void GetTriggerName_ReturnsPrefixedId()
    {
        _builder.GetTriggerName("t-orders").Should().Be("msosync__t-orders");
    }

    private static SyncTrigger MakeV2Trigger(string pkColumnsJson = """["order_id"]""") =>
        new()
        {
            TriggerId      = "t-orders",
            SourceTable    = "dbo.Orders",
            ChannelId      = "default",
            SyncOnInsert   = true,
            SyncOnUpdate   = true,
            SyncOnDelete   = true,
            PkColumnsJson  = pkColumnsJson
        };

    [Fact]
    public void BuildDdl_WithPkColumnsJson_ContainsPkDataDeclaration()
    {
        var ddl = _builder.BuildDdl(MakeV2Trigger(), "hub");
        ddl.Should().Contain("@pk_data");
    }

    [Fact]
    public void BuildDdl_WithPkColumnsJson_CapturesFromInserted()
    {
        var ddl = _builder.BuildDdl(MakeV2Trigger(), "hub");
        ddl.Should().Contain("[order_id] FROM inserted");
    }

    [Fact]
    public void BuildDdl_WithPkColumnsJson_CapturesFromDeletedForUpdate()
    {
        var ddl = _builder.BuildDdl(MakeV2Trigger(), "hub");
        ddl.Should().Contain("[order_id] FROM deleted");
    }

    [Fact]
    public void BuildDdl_WithCompositePkColumnsJson_CapturesBothColumns()
    {
        var ddl = _builder.BuildDdl(MakeV2Trigger("""["tenant_id","order_id"]"""), "hub");
        ddl.Should().Contain("[tenant_id],[order_id]");
    }

    [Fact]
    public void BuildDdl_WithNullPkColumnsJson_DoesNotContainPkData()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");  // PkColumnsJson = null
        ddl.Should().NotContain("@pk_data");
    }

    [Fact]
    public void BuildDdl_WithPkColumnsJson_IncludesPkDataInInsert()
    {
        var ddl = _builder.BuildDdl(MakeV2Trigger(), "hub");
        ddl.Should().Contain("pk_data");
    }
}
