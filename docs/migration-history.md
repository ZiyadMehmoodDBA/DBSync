# MSOSync Migration History

Migrations in apply order. All migrations target the `msosync` schema on SQL Server.

| # | Migration Name | Tables Created / Modified | Date |
|---|----------------|---------------------------|------|
| M001 | `20260619000001_CreateSchema` | Creates `msosync` schema | 2026-06-19 |
| M002 | `20260619000002_CoreTables` | Creates: `sync_node_group`, `sync_node`, `sync_node_security`, `sync_channel`, `sync_parameter`, `sync_parameter_hist` | 2026-06-19 |
| M003 | `20260619000003_TriggerAndRoutingTables` | Creates: `sync_trigger`, `sync_trigger_hist`, `sync_router`, `sync_trigger_router` | 2026-06-19 |
| M004 | `20260619000004_EventTables` | Creates: `sync_data_event`, `sync_data_event_batch` | 2026-06-19 |
| M005 | `20260619000005_BatchTables` | Creates: `sync_outgoing_batch`, `sync_incoming_batch`, `sync_batch_error` | 2026-06-19 |
| M006 | `20260619000006_MonitoringTables` | Creates: `sync_monitor`, `sync_runtime_stats`, `sync_audit` | 2026-06-19 |
| M007 | `20260619000007_SecurityTables` | Creates: `sync_user`, `sync_role`, `sync_user_role` | 2026-06-19 |
| M008 | `20260619000008_SeedData` | Seeds: roles (ADMIN/OPERATOR/VIEWER), default channel, default parameters | 2026-06-19 |
| M009 | `20260619000009_RefreshTokenAndLockout` | Adds lockout columns to `sync_user`; creates `sync_user_refresh_token` | 2026-06-19 |
| M010 | `20260619000010_NodeSecurityHashes` | Adds `current_token_hash`, `next_token_hash` to `sync_node_security` | 2026-06-19 |
| M011 | `20260619000011_RemovePlaintextNodeToken` | Drops `node_token` from `sync_node_security`; adds `rotation_scheduled` | 2026-06-19 |
| M012 | `20260619000012_Transport` | Adds `transport_mode` to `sync_node`; adds `batch_sequence`, `source_node_id`, `received_time` to `sync_incoming_batch` | 2026-06-19 |
| M013 | `20260623000000_M013_ApplyEngine` | Adds `pk_columns_json` to `sync_trigger` | 2026-06-23 |
| M014 | `M014_SecurityAndHeartbeat` | Adds heartbeat columns to `sync_node` (`upstream_node_id`, `last_probe_time`, `last_probe_latency_ms`, `connectivity_status`); adds `token_lookup_hash` to `sync_user_refresh_token` | 2026-06-23 |
| M015 | `20260624145922_M015_OperationalReadAPIs` | Adds `create_time` to `sync_batch_error`; adds performance indexes on `sync_data_event`, `sync_incoming_batch`, `sync_outgoing_batch`, `sync_audit` | 2026-06-24 |
| M016 | `M016_NodeDbConnection` | Adds DB connection columns to `sync_node` (`db_server`, `db_name`, `db_auth_mode`, `db_user`, `db_password_encrypted`) | 2026-06-24 |
| M017 | `20260702105508_M017_UserPreferences` | Creates `sync_user_preference` | 2026-07-02 |
| M018 | `20260702122912_M018_Permissions` | Creates `sync_permission`, `sync_role_permission`; seeds 12 permission keys and role assignments | 2026-07-02 |
| M019 | `20260704145235_M019_ExportJobs` | Creates `sync_export_job` | 2026-07-04 |
| M020 | `20260706115852_M020_AddRegistrationMetadata` | Adds `metadata_json`, `node_name`, `processed_at`, `processed_by`, `registration_status`, `registration_type`, `row_version` to `sync_registration_request`; adds indexes | 2026-07-06 |
| M021 | `20260706131159_M021_AddNodeTypeExternalId` | Adds `external_id`, `node_name`, `node_type` to `sync_node` | 2026-07-06 |
| M022 | `20260707074932_M022_NodeLifecycle` | Adds lifecycle/state columns to `sync_node`; creates `sync_node_bootstrap_token`, `sync_node_connectivity_history`, `sync_node_lifecycle_history`; seeds `PROVISION_NODES` and `MANAGE_NODE_LIFECYCLE` permissions | 2026-07-07 |
| M023 | `20260708091004_M023_ConfigurationManagement` | Creates `sync_configuration_template`, `sync_configuration_template_version`, `sync_node_configuration_override`, `sync_node_configuration_history`, `sync_configuration_rollout`; seeds `MANAGE_CONFIGURATIONS` permission | 2026-07-08 |
