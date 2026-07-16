-- Apply M031_CoreTopologyTenantId
-- Run against msosync_db
-- All DDL statements that reference new columns are in separate statements

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN

    -- ── 1. topology tables: NOT NULL tenant_id, backfill, index ──────────────

    -- sync_node
    ALTER TABLE [msosync].[sync_node]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_node_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';

    CREATE INDEX [IX_sync_node_tenant_id] ON [msosync].[sync_node] ([tenant_id]);

    -- sync_node_group
    ALTER TABLE [msosync].[sync_node_group]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_node_group_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';

    CREATE INDEX [IX_sync_node_group_tenant_id] ON [msosync].[sync_node_group] ([tenant_id]);

    -- sync_node_security
    ALTER TABLE [msosync].[sync_node_security]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_node_security_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';

    CREATE INDEX [IX_sync_node_security_tenant_id] ON [msosync].[sync_node_security] ([tenant_id]);

    -- sync_channel
    ALTER TABLE [msosync].[sync_channel]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_channel_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';

    CREATE INDEX [IX_sync_channel_tenant_id] ON [msosync].[sync_channel] ([tenant_id]);

    -- sync_trigger
    ALTER TABLE [msosync].[sync_trigger]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_trigger_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';

    CREATE INDEX [IX_sync_trigger_tenant_id] ON [msosync].[sync_trigger] ([tenant_id]);

    -- sync_trigger_hist
    ALTER TABLE [msosync].[sync_trigger_hist]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_trigger_hist_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';

    CREATE INDEX [IX_sync_trigger_hist_tenant_id] ON [msosync].[sync_trigger_hist] ([tenant_id]);

    -- sync_router
    ALTER TABLE [msosync].[sync_router]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_router_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';

    CREATE INDEX [IX_sync_router_tenant_id] ON [msosync].[sync_router] ([tenant_id]);

    -- sync_trigger_router
    ALTER TABLE [msosync].[sync_trigger_router]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_trigger_router_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';

    CREATE INDEX [IX_sync_trigger_router_tenant_id] ON [msosync].[sync_trigger_router] ([tenant_id]);

    PRINT 'Step 1a (8 main topology tables) done.';
END

GO

-- scope tables exist only if M027 was applied
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
    AND EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'msosync' AND t.name = 'sync_node_scope')
BEGIN
    ALTER TABLE [msosync].[sync_node_scope]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_node_scope_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';
    CREATE INDEX [IX_sync_node_scope_tenant_id] ON [msosync].[sync_node_scope] ([tenant_id]);
    PRINT 'sync_node_scope tenant_id done.';
END

GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
    AND EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'msosync' AND t.name = 'sync_node_channel')
BEGIN
    ALTER TABLE [msosync].[sync_node_channel]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_node_channel_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';
    CREATE INDEX [IX_sync_node_channel_tenant_id] ON [msosync].[sync_node_channel] ([tenant_id]);
    PRINT 'sync_node_channel tenant_id done.';
END

GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
    AND EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'msosync' AND t.name = 'sync_node_trigger')
BEGIN
    ALTER TABLE [msosync].[sync_node_trigger]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_node_trigger_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';
    CREATE INDEX [IX_sync_node_trigger_tenant_id] ON [msosync].[sync_node_trigger] ([tenant_id]);
    PRINT 'sync_node_trigger tenant_id done.';
END

GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
    AND EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'msosync' AND t.name = 'sync_node_router')
BEGIN
    ALTER TABLE [msosync].[sync_node_router]
        ADD [tenant_id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_sync_node_router_tenant_id] DEFAULT '00000000-0000-0000-0000-000000000001';
    CREATE INDEX [IX_sync_node_router_tenant_id] ON [msosync].[sync_node_router] ([tenant_id]);
    PRINT 'sync_node_router tenant_id done.';
END

GO

-- ── 2. FK: sync_node → tenant ────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    ALTER TABLE [msosync].[sync_node]
        ADD CONSTRAINT [FK_sync_node_tenant_id]
        FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant] ([tenant_id])
        ON DELETE NO ACTION;

    PRINT 'Step 2 (FK sync_node → tenant) done.';
END

GO

-- ── 3. Nullable tenant_id on hybrid tables ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    ALTER TABLE [msosync].[sync_role]            ADD [tenant_id] uniqueidentifier NULL;
    ALTER TABLE [msosync].[sync_user_role]       ADD [tenant_id] uniqueidentifier NULL;
    ALTER TABLE [msosync].[sync_user_preference] ADD [tenant_id] uniqueidentifier NULL;

    PRINT 'Step 3 (hybrid nullable tenant_id) done.';
END

GO

-- ── 4. sync_parameter: surrogate PK (id) + nullable tenant_id ────────────────────
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    -- 4a. Add id IDENTITY column
    ALTER TABLE [msosync].[sync_parameter]
        ADD [id] bigint IDENTITY(1,1) NOT NULL;

    PRINT 'Step 4a (sync_parameter.id added) done.';
END

GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    -- 4b. Drop old PK on parameter_name
    ALTER TABLE [msosync].[sync_parameter]
        DROP CONSTRAINT [PK_sync_parameter];

    -- 4c. New PK on id
    ALTER TABLE [msosync].[sync_parameter]
        ADD CONSTRAINT [PK_sync_parameter] PRIMARY KEY ([id]);

    PRINT 'Step 4b-4c (sync_parameter PK swapped) done.';
END

GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    -- 4d. Add nullable tenant_id to sync_parameter
    ALTER TABLE [msosync].[sync_parameter]
        ADD [tenant_id] uniqueidentifier NULL;

    -- 4e. Unique index on (parameter_name, tenant_id) — NULL-safe in SQL Server unique indexes
    CREATE UNIQUE INDEX [UX_sync_parameter_name_tenant]
        ON [msosync].[sync_parameter] ([parameter_name], [tenant_id]);

    PRINT 'Step 4d-4e (sync_parameter.tenant_id + unique index) done.';
END

GO

-- ── 5. sync_parameter_hist: nullable tenant_id ───────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    ALTER TABLE [msosync].[sync_parameter_hist]
        ADD [tenant_id] uniqueidentifier NULL;

    PRINT 'Step 5 (sync_parameter_hist.tenant_id) done.';
END

GO

-- ── 6. Rename sync_monitor → sync_monitor_snapshot ───────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.tables t
        JOIN sys.schemas s ON t.schema_id = s.schema_id
        WHERE s.name = 'msosync' AND t.name = 'sync_monitor'
    )
    BEGIN
        EXEC sp_rename '[msosync].[sync_monitor]', 'sync_monitor_snapshot';
        PRINT 'Step 6 (sync_monitor → sync_monitor_snapshot) done.';
    END
END

GO

-- ── 7. lock_scope on sync_lock ────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    ALTER TABLE [msosync].[sync_lock]
        ADD [lock_scope] int NOT NULL
        CONSTRAINT [DF_sync_lock_lock_scope] DEFAULT 0;

    PRINT 'Step 7 (sync_lock.lock_scope) done.';
END

GO

-- ── 8. Register migration ─────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'M031_CoreTopologyTenantId')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'M031_CoreTopologyTenantId', N'9.0.0');

    PRINT 'M031_CoreTopologyTenantId registered in __EFMigrationsHistory.';
END

GO
PRINT 'M031 apply script complete.';
