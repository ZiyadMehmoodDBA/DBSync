# MSOSync Migration History

All EF Core migrations in `src/MSOSync.Persistence/Migrations/`. Applied in order.

| Migration | File | Description | Epic |
|-----------|------|-------------|------|
| M001 | `M001_CreateSchema.cs` | Create `msosync` schema | 2 |
| M002 | `M002_CoreTables.cs` | Core tables: sync_node, sync_channel, sync_router | 2 |
| M003 | `M003_TriggerAndRoutingTables.cs` | sync_trigger, sync_router_rule | 2 |
| M004 | `M004_EventTables.cs` | sync_data_event, sync_incoming_event | 2 |
| M005 | `M005_BatchTables.cs` | sync_outgoing_batch, sync_batch_error, sync_incoming_batch | 2 |
| M006 | `M006_MonitoringTables.cs` | sync_lock, sync_metrics_snapshot | 2 |
| M007 | `M007_SecurityTables.cs` | sync_user, sync_role, sync_user_role, sync_node_security, sync_audit | 3 |
| M008 | `M008_SeedData.cs` | Seed: 3 roles (ADMIN/OPERATOR/VIEWER), default channel (config, priority 100), 6 parameters | 3 |
| M009 | `M009_RefreshTokenAndLockout.cs` | sync_user_refresh_token; LockedUntil + PasswordChangedAt on sync_user | 3 |
| M010 | `M010_NodeSecurityHashes.cs` | current_token_hash + next_token_hash on sync_node_security | 3 |
| M011 | `M011_RemovePlaintextNodeToken.cs` | Drop plaintext node_token column from sync_node | 4 |
| M012 | `M012_Transport.cs` | Transport columns on sync_node; sync_node_db_connection | 6 |
| M013 | `M013_ApplyEngine.cs` | pk_columns_json on sync_trigger | 7 |
| M014 | `M014_SecurityAndHeartbeat.cs` | Heartbeat columns on sync_node; token_lookup_hash on sync_user_refresh_token | 8 |
| M015 | `20260624145922_M015_OperationalReadAPIs.cs` | `create_time` column on `sync_batch_error`; performance indexes on `sync_data_event` and `sync_batch_error` | 9A |
| M016 | `M016_NodeDbConnection.cs` | DB connection columns (`db_server`, `db_name`, `db_auth_mode`, `db_user`, `db_password_encrypted`) on `sync_node` | 6/9B |
| M017 | `20260702105508_M017_UserPreferences.cs` | sync_user_preference | 11E |
| M018 | `20260702122912_M018_Permissions.cs` | sync_permission, sync_role_permission, sync_user_permission | 11F |
| M019 | `20260704145235_M019_ExportJobs.cs` | sync_export_job | 11G |
| M020 | `20260706115852_M020_AddRegistrationMetadata.cs` | Registration metadata columns on sync_registration_request | 12A |
| M021 | `20260706131159_M021_AddNodeTypeExternalId.cs` | node_type + external_id + node_name on sync_node | 12A |
| M022 | `20260707074932_M022_NodeLifecycle.cs` | NodeLifecycleState enum column; sync_node_lifecycle_history; sync_node_connectivity_history; sync_node_bootstrap_token; PROVISION_NODES + MANAGE_NODE_LIFECYCLE permission seeds | 12B-1 |
| M023 | `20260708091004_M023_ConfigurationManagement.cs` | sync_configuration_template; sync_configuration_template_version; sync_node_configuration_override; sync_node_configuration_history; sync_configuration_rollout; MANAGE_CONFIGURATIONS seed | 12B-2 |
| M024 | `M024_OperationsFoundation.cs` | sync_operation table + 4 indexes | 12C |
| M025 | `M025_ParameterMetadata.cs` | Metadata columns on sync_parameter (category, display_name, description, etc.); 10 new parameters (5 feature flags + 5 retention policies) | 12C |
| M026 | `20260709143230_M026_SnapshotSync.cs` | Correlation indexes on sync_audit, sync_node_lifecycle_history, sync_node_configuration_history | 12C |

**Total: 26 migrations, 36 tables in `msosync` schema as of M026.**

## Permission Inventory (15 total as of M026)

| Constant | String Value | Default Role |
|----------|-------------|--------------|
| `SystemPermissions.ViewEvents` | `VIEW_EVENTS` | VIEWER, OPERATOR, ADMIN |
| `SystemPermissions.ViewMetrics` | `VIEW_METRICS` | VIEWER, OPERATOR, ADMIN |
| `SystemPermissions.ViewAudit` | `VIEW_AUDIT` | VIEWER, OPERATOR, ADMIN |
| `SystemPermissions.ViewTopology` | `VIEW_TOPOLOGY` | VIEWER, OPERATOR, ADMIN |
| `SystemPermissions.ExportData` | `EXPORT_DATA` | OPERATOR, ADMIN |
| `SystemPermissions.RetryBatches` | `RETRY_BATCHES` | OPERATOR, ADMIN |
| `SystemPermissions.ApproveNodes` | `APPROVE_NODES` | OPERATOR, ADMIN |
| `SystemPermissions.ReleaseLocks` | `RELEASE_LOCKS` | OPERATOR, ADMIN |
| `SystemPermissions.EditParameters` | `EDIT_PARAMETERS` | OPERATOR, ADMIN |
| `SystemPermissions.ManageTriggers` | `MANAGE_TRIGGERS` | OPERATOR, ADMIN |
| `SystemPermissions.ManageRouters` | `MANAGE_ROUTERS` | OPERATOR, ADMIN |
| `SystemPermissions.ManageNodeLifecycle` | `MANAGE_NODE_LIFECYCLE` | OPERATOR, ADMIN |
| `SystemPermissions.ManageUsers` | `MANAGE_USERS` | ADMIN only |
| `SystemPermissions.ProvisionNodes` | `PROVISION_NODES` | ADMIN only |
| `SystemPermissions.ManageConfigurations` | `MANAGE_CONFIGURATIONS` | ADMIN only |
