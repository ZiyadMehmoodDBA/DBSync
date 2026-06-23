using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public sealed class InsertBuilder
{
    public SqlStatement Build(string schemaName, string tableName, JsonElement rowData)
    {
        var columns    = new List<string>();
        var paramNames = new List<string>();
        var parameters = new List<SqlParameter>();
        int i = 0;

        foreach (var prop in rowData.EnumerateObject())
        {
            columns.Add($"[{prop.Name}]");
            paramNames.Add($"@p{i}");
            parameters.Add(CreateParameter($"@p{i}", prop.Value));
            i++;
        }

        if (columns.Count == 0)
            throw new ArgumentException("rowData must contain at least one property for INSERT");

        var sql = $"INSERT INTO [{schemaName}].[{tableName}] ({string.Join(",", columns)}) VALUES ({string.Join(",", paramNames)})";
        return new SqlStatement(sql, parameters.AsReadOnly());
    }

    internal static SqlParameter CreateParameter(string name, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null   => new SqlParameter(name, DBNull.Value),
        JsonValueKind.True   => new SqlParameter(name, true),
        JsonValueKind.False  => new SqlParameter(name, false),
        JsonValueKind.Number => CreateNumberParameter(name, value),
        JsonValueKind.String => new SqlParameter(name, value.GetString()),
        _ => throw new InvalidOperationException($"Unsupported JSON token type: {value.ValueKind}")
    };

    private static SqlParameter CreateNumberParameter(string name, JsonElement value)
    {
        if (value.TryGetInt32(out var i32)) return new SqlParameter(name, i32);
        if (value.TryGetInt64(out var i64)) return new SqlParameter(name, i64);
        if (value.TryGetDecimal(out var dec)) return new SqlParameter(name, dec);
        if (value.TryGetDouble(out var dbl)) return new SqlParameter(name, dbl);
        return new SqlParameter(name, value.GetRawText());
    }
}
