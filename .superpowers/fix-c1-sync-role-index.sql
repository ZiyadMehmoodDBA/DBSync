-- Fix C-1: Change sync_role unique index from (role_name) to (role_name, tenant_id)
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_sync_role_role_name' AND object_id = OBJECT_ID('[msosync].[sync_role]'))
BEGIN
    DROP INDEX [UQ_sync_role_role_name] ON [msosync].[sync_role];
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_sync_role_role_name_tenant_id' AND object_id = OBJECT_ID('[msosync].[sync_role]'))
BEGIN
    CREATE UNIQUE INDEX [UQ_sync_role_role_name_tenant_id] ON [msosync].[sync_role] ([role_name], [tenant_id]);
END
GO
