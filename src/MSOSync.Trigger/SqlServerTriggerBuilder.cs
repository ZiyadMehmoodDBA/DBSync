// src/MSOSync.Trigger/SqlServerTriggerBuilder.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Trigger;

public sealed class SqlServerTriggerBuilder
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public string BuildDdl(SyncTrigger trigger, string nodeId)
    {
        var triggerName = $"msosync__{trigger.TriggerId}";
        var parts = trigger.SourceTable.Split('.', 2);
        var tableSchema = parts.Length == 2 ? parts[0] : "dbo";
        var tableName   = parts.Length == 2 ? parts[1] : parts[0];

        var events = new List<string>();
        if (trigger.SyncOnInsert) events.Add("INSERT");
        if (trigger.SyncOnUpdate) events.Add("UPDATE");
        if (trigger.SyncOnDelete) events.Add("DELETE");
        var afterClause = string.Join(", ", events);

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
    }

    public string GetTriggerName(string triggerId) => $"msosync__{triggerId}";
}
