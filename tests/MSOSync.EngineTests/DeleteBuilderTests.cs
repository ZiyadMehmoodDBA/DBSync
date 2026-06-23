// tests/MSOSync.EngineTests/DeleteBuilderTests.cs
using System.Text.Json;
using FluentAssertions;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class DeleteBuilderTests
{
    private readonly DeleteBuilder _builder = new();

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Build_SinglePk_GeneratesCorrectSql()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"order_id":42}"""));
        stmt.CommandText.Should().Be("DELETE FROM [dbo].[orders] WHERE [order_id]=@pk0");
        stmt.Parameters.Should().HaveCount(1);
        stmt.Parameters[0].Value.Should().Be(42);
    }

    [Fact]
    public void Build_CompositePk_GeneratesAndClause()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"tenant_id":1,"order_id":42}"""));
        stmt.CommandText.Should().Be("DELETE FROM [dbo].[orders] WHERE [tenant_id]=@pk0 AND [order_id]=@pk1");
        stmt.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void Build_EmptyPkData_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("{}"));
        act.Should().Throw<ArgumentException>();
    }
}
