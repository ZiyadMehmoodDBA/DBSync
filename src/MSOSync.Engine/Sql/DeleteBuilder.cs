using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MSOSync.Engine;

public sealed class DeleteBuilder
{
    public SqlStatement Build(string schemaName, string tableName, JsonElement pkData)
    {
        var whereParts = new List<string>();
        var parameters = new List<SqlParameter>();
        int pk = 0;

        foreach (var prop in pkData.EnumerateObject())
        {
            whereParts.Add($"[{prop.Name}]=@pk{pk}");
            parameters.Add(InsertBuilder.CreateParameter($"@pk{pk}", prop.Value));
            pk++;
        }

        if (whereParts.Count == 0)
            throw new ArgumentException("pkData must contain at least one property for DELETE WHERE clause");

        var sql = $"DELETE FROM [{schemaName}].[{tableName}] WHERE {string.Join(" AND ", whereParts)}";
        return new SqlStatement(sql, parameters.AsReadOnly());
    }
}
