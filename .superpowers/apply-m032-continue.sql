-- ============================================================
-- M032 (continuation): Domain TenantId Migration
-- Picks up after sync_configuration_template (tables 1-11 done)
-- Remaining: sync_configuration_template_version through sync_export_job
-- ============================================================

SET QUOTED_IDENTIFIER ON;
GO

-- ---------------------------------------------------------
-- sync_configuration_template_version
-- (tenant_id NULL column was added in previous run; backfill + NOT NULL + index + FK remain)
-- ---------------------------------------------------------

SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_configuration_template_version] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_configuration_template_version] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
CREATE NONCLUSTERED INDEX [IX_sync_configuration_template_version_TenantId_TemplateId]
    ON [msosync].[sync_configuration_template_version] ([tenant_id] ASC, [template_id] ASC);
GO
ALTER TABLE [msosync].[sync_configuration_template_version]
    ADD CONSTRAINT [FK_sync_configuration_template_version_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- sync_node_configuration_override
-- ---------------------------------------------------------

SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_node_configuration_override] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_node_configuration_override] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_node_configuration_override] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
CREATE NONCLUSTERED INDEX [IX_sync_node_configuration_override_TenantId_NodeId]
    ON [msosync].[sync_node_configuration_override] ([tenant_id] ASC, [node_id] ASC);
GO
ALTER TABLE [msosync].[sync_node_configuration_override]
    ADD CONSTRAINT [FK_sync_node_configuration_override_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- sync_node_configuration_history
-- ---------------------------------------------------------

SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_node_configuration_history] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_node_configuration_history] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_node_configuration_history] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
CREATE NONCLUSTERED INDEX [IX_sync_node_configuration_history_TenantId_NodeId]
    ON [msosync].[sync_node_configuration_history] ([tenant_id] ASC, [node_id] ASC);
GO
ALTER TABLE [msosync].[sync_node_configuration_history]
    ADD CONSTRAINT [FK_sync_node_configuration_history_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ---------------------------------------------------------
-- sync_configuration_rollout
-- ---------------------------------------------------------

SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_configuration_rollout] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_configuration_rollout] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_configuration_rollout] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
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
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_runtime_stats] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_runtime_stats] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_runtime_stats] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
CREATE NONCLUSTERED INDEX [IX_sync_runtime_stats_TenantId_CreateTime]
    ON [msosync].[sync_runtime_stats] ([tenant_id] ASC, [create_time] DESC);
GO
ALTER TABLE [msosync].[sync_runtime_stats]
    ADD CONSTRAINT [FK_sync_runtime_stats_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_audit
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_audit] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_audit] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_audit] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
CREATE NONCLUSTERED INDEX [IX_sync_audit_TenantId_CreateTime]
    ON [msosync].[sync_audit] ([tenant_id] ASC, [create_time] DESC);
GO
ALTER TABLE [msosync].[sync_audit]
    ADD CONSTRAINT [FK_sync_audit_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_operation
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_operation] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_operation] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_operation] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
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

-- sync_notification (column: created_at, not create_time)
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_notification] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_notification] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_notification] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
CREATE NONCLUSTERED INDEX [IX_sync_notification_TenantId_CreatedAt]
    ON [msosync].[sync_notification] ([tenant_id] ASC, [created_at] DESC);
GO
ALTER TABLE [msosync].[sync_notification]
    ADD CONSTRAINT [FK_sync_notification_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_user_notification
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_user_notification] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_user_notification] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_user_notification] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
CREATE NONCLUSTERED INDEX [IX_sync_user_notification_TenantId_UserId]
    ON [msosync].[sync_user_notification] ([tenant_id] ASC, [user_id] ASC);
GO
ALTER TABLE [msosync].[sync_user_notification]
    ADD CONSTRAINT [FK_sync_user_notification_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_user_refresh_token
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_user_refresh_token] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [msosync].[sync_user_refresh_token] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [msosync].[sync_user_refresh_token] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
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
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [dbo].[sync_export_job] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
GO
SET QUOTED_IDENTIFIER ON;
UPDATE [dbo].[sync_export_job] SET [tenant_id] = '00000000-0000-0000-0000-000000000001' WHERE [tenant_id] IS NULL;
GO
SET QUOTED_IDENTIFIER ON;
ALTER TABLE [dbo].[sync_export_job] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
GO
SET QUOTED_IDENTIFIER ON;
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
