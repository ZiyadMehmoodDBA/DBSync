// tests/MSOSync.EngineTests/UpdateBuilderTests.cs
using System.Text.Json;
using FluentAssertions;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class UpdateBuilderTests
{
    private readonly UpdateBuilder _builder = new();

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Build_SinglePk_SingleSet_GeneratesCorrectSql()
    {
        var stmt = _builder.Build("dbo", "orders",
            Json("""{"order_id":10}"""),
            Json("""{"status":"closed"}"""));

        stmt.CommandText.Should().Be("UPDATE [dbo].[orders] SET [status]=@p0 WHERE [order_id]=@pk0");
        stmt.Parameters.Should().HaveCount(2);
        stmt.Parameters[0].Value.Should().Be("closed");
        stmt.Parameters[1].Value.Should().Be(10);
    }

    [Fact]
    public void Build_CompositePk_GeneratesAndClause()
    {
        var stmt = _builder.Build("dbo", "orders",
            Json("""{"tenant_id":1,"order_id":42}"""),
            Json("""{"status":"done"}"""));

        stmt.CommandText.Should().Contain("WHERE [tenant_id]=@pk0 AND [order_id]=@pk1");
    }

    [Fact]
    public void Build_EmptyRowData_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("""{"id":1}"""), Json("{}"));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_EmptyPkData_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("{}"), Json("""{"status":"x"}"""));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_PkColumnAlsoInRowData_ParametersCorrect()
    {
        // UPDATE orders SET order_id=@p0 WHERE order_id=@pk0 (PK change scenario)
        var stmt = _builder.Build("dbo", "orders",
            Json("""{"order_id":10}"""),
            Json("""{"order_id":20,"status":"updated"}"""));

        stmt.CommandText.Should().Contain("WHERE [order_id]=@pk0");
        stmt.Parameters.Should().HaveCount(3);
    }
}
