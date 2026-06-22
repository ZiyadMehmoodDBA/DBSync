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
}
