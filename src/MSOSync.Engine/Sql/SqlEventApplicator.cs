using System.Text.Json;

namespace MSOSync.Engine;

public sealed class SqlEventApplicator(
    InsertBuilder insert,
    UpdateBuilder update,
    DeleteBuilder delete) : ISqlEventApplicator
{
    public SqlStatement BuildInsert(string schemaName, string tableName, JsonElement rowData)
        => insert.Build(schemaName, tableName, rowData);

    public SqlStatement BuildUpdate(string schemaName, string tableName, JsonElement pkData, JsonElement rowData)
        => update.Build(schemaName, tableName, pkData, rowData);

    public SqlStatement BuildDelete(string schemaName, string tableName, JsonElement pkData)
        => delete.Build(schemaName, tableName, pkData);
}
