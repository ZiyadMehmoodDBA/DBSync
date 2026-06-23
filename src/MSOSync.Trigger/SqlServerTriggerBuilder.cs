// src/MSOSync.Trigger/SqlServerTriggerBuilder.cs
using System.Text.Json;
using MSOSync.Persistence.Entities;

namespace MSOSync.Trigger;

public sealed class SqlServerTriggerBuilder
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    private static void ValidateName(string value, string fieldName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[\[\]a-zA-Z_][a-zA-Z0-9_\.\[\]]*$"))
            throw new ArgumentException($"Invalid characters in {fieldName}: {value}");
        if (value.Contains('\''))
            throw new ArgumentException($"Single quote not allowed in {fieldName}");
    }

    public string BuildDdl(SyncTrigger trigger, string nodeId)
    {
        ValidateName(trigger.SourceTable, nameof(trigger.SourceTable));
        ValidateName(trigger.ChannelId, nameof(trigger.ChannelId));
        var triggerName = $"msosync__{trigger.TriggerId}";
        var parts = trigger.SourceTable.Split('.', 2);
        var tableSchema = parts.Length == 2 ? parts[0] : "dbo";
        var tableName   = parts.Length == 2 ? parts[1] : parts[0];

        var events = new List<string>();
        if (trigger.SyncOnInsert) events.Add("INSERT");
        if (trigger.SyncOnUpdate) events.Add("UPDATE");
        if (trigger.SyncOnDelete) events.Add("DELETE");
        var afterClause = string.Join(", ", events);

        string[]? pkColumns = null;
        if (!string.IsNullOrWhiteSpace(trigger.PkColumnsJson))
            pkColumns = JsonSerializer.Deserialize<string[]>(trigger.PkColumnsJson);

        if (pkColumns != null && pkColumns.Length > 0)
            return BuildV2Ddl(triggerName, tableSchema, tableName, afterClause, nodeId, trigger, pkColumns);

        return BuildV1Ddl(triggerName, tableSchema, tableName, afterClause, nodeId, trigger);
    }

    private string BuildV1Ddl(string triggerName, string tableSchema, string tableName,
        string afterClause, string nodeId, SyncTrigger trigger) => $"""
            CREATE OR ALTER TRIGGER [{triggerName}]
            ON [{tableSchema}].[{tableName}]
            AFTER {afterClause}
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @event_type CHAR(1) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted) THEN 'U'
                        WHEN EXISTS(SELECT 1 FROM inserted) THEN 'I'
                        ELSE 'D'
                    END;
                DECLARE @row_data NVARCHAR(MAX) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted)
                            THEN (SELECT * FROM inserted FOR JSON PATH)
                        ELSE (SELECT * FROM deleted FOR JSON PATH)
                    END;
                INSERT INTO [{Schema}].[sync_data_event]
                    (trigger_id, source_node_id, channel_id, event_type, table_name,
                     row_data, transaction_id, create_time, is_processed)
                VALUES (
                    '{trigger.TriggerId}',
                    N'{nodeId}',
                    '{trigger.ChannelId}',
                    @event_type,
                    '{trigger.SourceTable}',
                    @row_data,
                    CURRENT_TRANSACTION_ID(),
                    GETUTCDATE(),
                    0
                );
            END
            """;

    private string BuildV2Ddl(string triggerName, string tableSchema, string tableName,
        string afterClause, string nodeId, SyncTrigger trigger, string[] pkColumns)
    {
        var pkColsSql = string.Join(",", pkColumns.Select(c => $"[{c}]"));
        return $"""
            CREATE OR ALTER TRIGGER [{triggerName}]
            ON [{tableSchema}].[{tableName}]
            AFTER {afterClause}
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @event_type CHAR(1) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted) THEN 'U'
                        WHEN EXISTS(SELECT 1 FROM inserted) THEN 'I'
                        ELSE 'D'
                    END;
                DECLARE @row_data NVARCHAR(MAX) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted)
                            THEN (SELECT * FROM inserted FOR JSON PATH)
                        ELSE NULL
                    END;
                DECLARE @pk_data NVARCHAR(MAX) =
                    CASE
                        WHEN EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted)
                            THEN (SELECT {pkColsSql} FROM deleted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
                        WHEN EXISTS(SELECT 1 FROM inserted)
                            THEN (SELECT {pkColsSql} FROM inserted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
                        ELSE (SELECT {pkColsSql} FROM deleted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
                    END;
                INSERT INTO [{Schema}].[sync_data_event]
                    (trigger_id, source_node_id, channel_id, event_type, table_name,
                     pk_data, row_data, transaction_id, create_time, is_processed)
                VALUES (
                    '{trigger.TriggerId}',
                    N'{nodeId}',
                    '{trigger.ChannelId}',
                    @event_type,
                    '{trigger.SourceTable}',
                    @pk_data,
                    @row_data,
                    CURRENT_TRANSACTION_ID(),
                    GETUTCDATE(),
                    0
                );
            END
            """;
    }

    public string GetTriggerName(string triggerId) => $"msosync__{triggerId}";
}
