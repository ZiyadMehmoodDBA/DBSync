using System.Text.Json;

namespace MSOSync.Engine;

public interface ISqlEventApplicator
{
    SqlStatement BuildInsert(string schemaName, string tableName, JsonElement rowData);
    SqlStatement BuildUpdate(string schemaName, string tableName, JsonElement pkData, JsonElement rowData);
    SqlStatement BuildDelete(string schemaName, string tableName, JsonElement pkData);
}
