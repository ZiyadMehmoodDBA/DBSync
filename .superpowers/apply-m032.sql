-- ============================================================
-- M032: Domain TenantId Migration — Part 1 (tables 1-11 + sync_configuration_template_version ADD COLUMN)
-- 21 tables: NULL -> backfill -> NOT NULL -> composite index -> FK
-- SystemTenant: 00000000-0000-0000-0000-000000000001
-- Note: DECLARE variables do not cross GO batch boundaries in sqlcmd,
--       so the SystemTenant GUID is inlined in each UPDATE.
--
-- IMPORTANT: Run apply-m032-continue.sql immediately after this script to complete the migration.
-- SET QUOTED_IDENTIFIER ON is required for the filtered index on sync_configuration_template_version.
-- ============================================================

SET QUOTED_IDENTIFIER ON;
GO

-- ---------------------------------------------------------
-- GROUP 1: Node Management (msosync)
-- ---------------------------------------------------------

-- sync_registration_request
ALTER TABLE [msosync].[sync_registration_request] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_registration_request] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
-- Verify: SELECT COUNT(*) FROM [msosync].[sync_registration_request] WHERE [tenant_id] IS NULL; -- Expected: 0
ALTER TABLE [msosync].[sync_registration_request] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_registration_request_TenantId_Status]
    ON [msosync].[sync_registration_request] ([tenant_id] ASC, [registration_status] ASC);
GO
ALTER TABLE [msosync].[sync_registration_request]
    ADD CONSTRAINT [FK_sync_registration_request_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_bootstrap_token
ALTER TABLE [msosync].[sync_node_bootstrap_token] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_node_bootstrap_token] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_node_bootstrap_token] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_node_bootstrap_token_TenantId_NodeId]
    ON [msosync].[sync_node_bootstrap_token] ([tenant_id] ASC, [node_id] ASC);
GO
ALTER TABLE [msosync].[sync_node_bootstrap_token]
    ADD CONSTRAINT [FK_sync_node_bootstrap_token_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_lifecycle_history
ALTER TABLE [msosync].[sync_node_lifecycle_history] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_node_lifecycle_history] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_node_lifecycle_history] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_node_lifecycle_history_TenantId_NodeId]
    ON [msosync].[sync_node_lifecycle_history] ([tenant_id] ASC, [node_id] ASC);
GO
ALTER TABLE [msosync].[sync_node_lifecycle_history]
    ADD CONSTRAINT [FK_sync_node_lifecycle_history_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_connectivity_history
ALTER TABLE [msosync].[sync_node_connectivity_history] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_node_connectivity_history] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_node_connectivity_history] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_node_connectivity_history_TenantId_NodeId]
    ON [msosync].[sync_node_connectivity_history] ([tenant_id] ASC, [node_id] ASC);
GO
ALTER TABLE [msosync].[sync_node_connectivity_history]
    ADD CONSTRAINT [FK_sync_node_connectivity_history_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- GROUP 2: Synchronization Engine (msosync)
-- ---------------------------------------------------------

-- sync_data_event
ALTER TABLE [msosync].[sync_data_event] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_data_event] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_data_event] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_data_event_TenantId_CreateTime]
    ON [msosync].[sync_data_event] ([tenant_id] ASC, [create_time] DESC);
GO
ALTER TABLE [msosync].[sync_data_event]
    ADD CONSTRAINT [FK_sync_data_event_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_data_event_batch
ALTER TABLE [msosync].[sync_data_event_batch] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_data_event_batch] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_data_event_batch] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_data_event_batch_TenantId_BatchId]
    ON [msosync].[sync_data_event_batch] ([tenant_id] ASC, [batch_id] ASC);
GO
ALTER TABLE [msosync].[sync_data_event_batch]
    ADD CONSTRAINT [FK_sync_data_event_batch_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_outgoing_batch
ALTER TABLE [msosync].[sync_outgoing_batch] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_outgoing_batch] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_outgoing_batch] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_outgoing_batch_TenantId_Status]
    ON [msosync].[sync_outgoing_batch] ([tenant_id] ASC, [status] ASC);
GO
ALTER TABLE [msosync].[sync_outgoing_batch]
    ADD CONSTRAINT [FK_sync_outgoing_batch_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_incoming_batch
ALTER TABLE [msosync].[sync_incoming_batch] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_incoming_batch] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_incoming_batch] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_incoming_batch_TenantId_Status]
    ON [msosync].[sync_incoming_batch] ([tenant_id] ASC, [status] ASC);
GO
ALTER TABLE [msosync].[sync_incoming_batch]
    ADD CONSTRAINT [FK_sync_incoming_batch_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_batch_error
ALTER TABLE [msosync].[sync_batch_error] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_batch_error] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_batch_error] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_batch_error_TenantId_CreateTime]
    ON [msosync].[sync_batch_error] ([tenant_id] ASC, [create_time] DESC);
GO
ALTER TABLE [msosync].[sync_batch_error]
    ADD CONSTRAINT [FK_sync_batch_error_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- GROUP 3: Configuration Management (msosync)
-- ---------------------------------------------------------

-- sync_configuration_template (+ unique constraint migration)
ALTER TABLE [msosync].[sync_configuration_template] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_configuration_template] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_configuration_template] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
-- Convert global unique name -> per-tenant unique (tenant_id, name)
DROP INDEX [UX_sync_configuration_template_name] ON [msosync].[sync_configuration_template];
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_sync_configuration_template_tenant_id_name]
    ON [msosync].[sync_configuration_template] ([tenant_id] ASC, [name] ASC);
GO
CREATE NONCLUSTERED INDEX [IX_sync_configuration_template_TenantId]
    ON [msosync].[sync_configuration_template] ([tenant_id] ASC);
GO
ALTER TABLE [msosync].[sync_configuration_template]
    ADD CONSTRAINT [FK_sync_configuration_template_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_configuration_template_version
ALTER TABLE [msosync].[sync_configuration_template_version] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_configuration_template_version] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_configuration_template_version] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_configuration_template_version_TenantId_TemplateId]
    ON [msosync].[sync_configuration_template_version] ([tenant_id] ASC, [template_id] ASC);
GO
ALTER TABLE [msosync].[sync_configuration_template_version]
    ADD CONSTRAINT [FK_sync_configuration_template_version_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_configuration_override
ALTER TABLE [msosync].[sync_node_configuration_override] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_node_configuration_override] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_node_configuration_override] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_node_configuration_override_TenantId_NodeId]
    ON [msosync].[sync_node_configuration_override] ([tenant_id] ASC, [node_id] ASC);
GO
ALTER TABLE [msosync].[sync_node_configuration_override]
    ADD CONSTRAINT [FK_sync_node_configuration_override_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_configuration_history
ALTER TABLE [msosync].[sync_node_configuration_history] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_node_configuration_history] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_node_configuration_history] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_node_configuration_history_TenantId_NodeId]
    ON [msosync].[sync_node_configuration_history] ([tenant_id] ASC, [node_id] ASC);
GO
ALTER TABLE [msosync].[sync_node_configuration_history]
    ADD CONSTRAINT [FK_sync_node_configuration_history_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_configuration_rollout
ALTER TABLE [msosync].[sync_configuration_rollout] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_configuration_rollout] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_configuration_rollout] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_configuration_rollout_TenantId_Status]
    ON [msosync].[sync_configuration_rollout] ([tenant_id] ASC, [status] ASC);
GO
ALTER TABLE [msosync].[sync_configuration_rollout]
    ADD CONSTRAINT [FK_sync_configuration_rollout_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- GROUP 4: Operations & Audit (msosync)
-- ---------------------------------------------------------

-- sync_runtime_stats
ALTER TABLE [msosync].[sync_runtime_stats] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_runtime_stats] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_runtime_stats] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_runtime_stats_TenantId_CreateTime]
    ON [msosync].[sync_runtime_stats] ([tenant_id] ASC, [create_time] DESC);
GO
ALTER TABLE [msosync].[sync_runtime_stats]
    ADD CONSTRAINT [FK_sync_runtime_stats_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_audit
ALTER TABLE [msosync].[sync_audit] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_audit] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_audit] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_audit_TenantId_CreateTime]
    ON [msosync].[sync_audit] ([tenant_id] ASC, [create_time] DESC);
GO
ALTER TABLE [msosync].[sync_audit]
    ADD CONSTRAINT [FK_sync_audit_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_operation
ALTER TABLE [msosync].[sync_operation] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_operation] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_operation] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_operation_TenantId_Status]
    ON [msosync].[sync_operation] ([tenant_id] ASC, [status] ASC);
GO
ALTER TABLE [msosync].[sync_operation]
    ADD CONSTRAINT [FK_sync_operation_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- GROUP 5: User & Runtime (msosync)
-- ---------------------------------------------------------

-- sync_notification
-- NOTE: column is created_at (not create_time) -- confirmed in M028_Notifications + SyncNotificationConfiguration
ALTER TABLE [msosync].[sync_notification] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_notification] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_notification] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_notification_TenantId_CreatedAt]
    ON [msosync].[sync_notification] ([tenant_id] ASC, [created_at] DESC);
GO
ALTER TABLE [msosync].[sync_notification]
    ADD CONSTRAINT [FK_sync_notification_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_user_notification
ALTER TABLE [msosync].[sync_user_notification] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_user_notification] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_user_notification] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_user_notification_TenantId_UserId]
    ON [msosync].[sync_user_notification] ([tenant_id] ASC, [user_id] ASC);
GO
ALTER TABLE [msosync].[sync_user_notification]
    ADD CONSTRAINT [FK_sync_user_notification_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_user_refresh_token
ALTER TABLE [msosync].[sync_user_refresh_token] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [msosync].[sync_user_refresh_token] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [msosync].[sync_user_refresh_token] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_user_refresh_token_TenantId_UserId]
    ON [msosync].[sync_user_refresh_token] ([tenant_id] ASC, [user_id] ASC);
GO
ALTER TABLE [msosync].[sync_user_refresh_token]
    ADD CONSTRAINT [FK_sync_user_refresh_token_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- dbo schema tables
-- ---------------------------------------------------------

-- sync_export_job (dbo schema)
ALTER TABLE [dbo].[sync_export_job] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
UPDATE [dbo].[sync_export_job] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
ALTER TABLE [dbo].[sync_export_job] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
CREATE NONCLUSTERED INDEX [IX_sync_export_job_TenantId_Status]
    ON [dbo].[sync_export_job] ([tenant_id] ASC, [status] ASC);
GO
ALTER TABLE [dbo].[sync_export_job]
    ADD CONSTRAINT [FK_sync_export_job_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- Mark migration as applied in EF history
-- ---------------------------------------------------------

INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES ('M032_DomainTenantIdMigration', '9.0.0');
GO

PRINT 'M032_DomainTenantIdMigration applied successfully.';
GO
