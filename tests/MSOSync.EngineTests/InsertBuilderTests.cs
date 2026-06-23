// tests/MSOSync.EngineTests/InsertBuilderTests.cs
using System.Text.Json;
using FluentAssertions;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class InsertBuilderTests
{
    private readonly InsertBuilder _builder = new();

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Build_SingleColumn_GeneratesCorrectSql()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"order_id":42}"""));
        stmt.CommandText.Should().Be("INSERT INTO [dbo].[orders] ([order_id]) VALUES (@p0)");
        stmt.Parameters.Should().HaveCount(1);
        stmt.Parameters[0].Value.Should().Be(42);
    }

    [Fact]
    public void Build_MultipleColumns_CorrectOrder()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"order_id":1,"status":"open"}"""));
        stmt.CommandText.Should().Contain("[order_id]").And.Contain("[status]");
        stmt.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void Build_NullValue_UsesDbNull()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"note":null}"""));
        stmt.Parameters[0].Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void Build_StringValue_PassedAsString()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"status":"closed"}"""));
        stmt.Parameters[0].Value.Should().Be("closed");
    }

    [Fact]
    public void Build_BoolTrue_PassedAsBool()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"active":true}"""));
        stmt.Parameters[0].Value.Should().Be(true);
    }

    [Fact]
    public void Build_BoolFalse_PassedAsBool()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"active":false}"""));
        stmt.Parameters[0].Value.Should().Be(false);
    }

    [Fact]
    public void Build_Int64Value_PassedAsLong()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"big":9999999999}"""));
        stmt.Parameters[0].Value.Should().Be(9999999999L);
    }

    [Fact]
    public void Build_DecimalValue_PassedAsDecimal()
    {
        var stmt = _builder.Build("dbo", "orders", Json("""{"price":12.99}"""));
        ((decimal)stmt.Parameters[0].Value).Should().Be(12.99m);
    }

    [Fact]
    public void Build_GuidString_PassedAsString()
    {
        var guid = Guid.NewGuid().ToString();
        var stmt = _builder.Build("dbo", "orders", Json($"{{\"id\":\"{guid}\"}}"));
        stmt.Parameters[0].Value.Should().Be(guid);
    }

    [Fact]
    public void Build_EmptyObject_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("{}"));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_UnsupportedTokenType_Throws()
    {
        var act = () => _builder.Build("dbo", "orders", Json("""{"nested":{"x":1}}"""));
        act.Should().Throw<InvalidOperationException>();
    }
}
