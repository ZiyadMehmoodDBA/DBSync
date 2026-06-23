using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public sealed class UpdateBuilder
{
    public SqlStatement Build(string schemaName, string tableName, JsonElement pkData, JsonElement rowData)
    {
        var setParts   = new List<string>();
        var whereParts = new List<string>();
        var parameters = new List<SqlParameter>();
        int i = 0;

        foreach (var prop in rowData.EnumerateObject())
        {
            setParts.Add($"[{prop.Name}]=@p{i}");
            parameters.Add(InsertBuilder.CreateParameter($"@p{i}", prop.Value));
            i++;
        }

        int pk = 0;
        foreach (var prop in pkData.EnumerateObject())
        {
            whereParts.Add($"[{prop.Name}]=@pk{pk}");
            parameters.Add(InsertBuilder.CreateParameter($"@pk{pk}", prop.Value));
            pk++;
        }

        if (setParts.Count == 0)
            throw new ArgumentException("rowData must contain at least one property for UPDATE");
        if (whereParts.Count == 0)
            throw new ArgumentException("pkData must contain at least one property for UPDATE WHERE clause");

        var sql = $"UPDATE [{schemaName}].[{tableName}] SET {string.Join(",", setParts)} WHERE {string.Join(" AND ", whereParts)}";
        return new SqlStatement(sql, parameters.AsReadOnly());
    }
}
