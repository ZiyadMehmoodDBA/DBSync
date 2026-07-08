# Epic 12B-2: Configuration Management — Design Spec

**Date:** 2026-07-08
**CTO Approval:** All six design sections reviewed and approved with refinements incorporated.
**Status:** Frozen — ready for implementation planning.

---

## Overview

Epic 12B-2 adds a professional, deterministic configuration management system for MSOSync Community Edition. It gives operators a structured way to author node configuration templates, version them immutably, assign them to nodes, and detect drift — all with full audit trail and real-time SignalR visibility.

**Core principles (CTO-approved, never violate):**

- Drafts are editable; published versions are immutable forever.
- Publishing never affects any running node.
- Assignment is the only deployment mechanism.
- Every node has exactly one assigned template (or none).
- Rollback is simply reassignment to an earlier published version.
- Drift is determined by Assigned Version + Applied Version + Configuration Hash.
- Every publish, assignment, and rollback is audited.
- REST is the source of truth. SignalR is a notification mechanism.
- Templates describe **how** a node operates. `SyncNode` describes **which** node it is.

---

## Section 1: Data Model

### New tables (M023 migration)

#### `sync_configuration_template`
| Column | Type | Notes |
|--------|------|-------|
| `id` | uniqueidentifier PK | |
| `name` | nvarchar(200) | unique |
| `description` | nvarchar(1000) | nullable |
| `status` | nvarchar(20) | Draft / Published / Archived |
| `current_published_version` | int | nullable; highest published version number |
| `latest_draft_version` | int | nullable; version number of the current draft row |
| `created_by` | uniqueidentifier FK→SyncUser | |
| `created_at` | datetime2 | |
| `updated_at` | datetime2 | |

#### `sync_configuration_template_version`
| Column | Type | Notes |
|--------|------|-------|
| `id` | uniqueidentifier PK | |
| `template_id` | uniqueidentifier FK | |
| `version_number` | int | unique per template |
| `is_draft` | bit | true = mutable draft; false = immutable published |
| `settings_json` | nvarchar(max) | serialized `ConfigurationSettings` |
| `template_content_hash` | nvarchar(64) | SHA256 of canonical settings_json (null while draft) |
| `schema_version` | int | current = 1; guards future shape changes |
| `row_version` | rowversion | optimistic concurrency on draft edits → 409 |
| `published_at` | datetime2 | nullable; set on publish |
| `published_by` | uniqueidentifier FK→SyncUser | nullable |

**Constraints:**
- Unique: `(template_id, version_number)`
- Filtered unique: `(template_id) WHERE is_draft = 1` — enforces at-most-one draft per template

**SchemaVersion compatibility rule:** server supports `SchemaVersion` 1..N where N = current. Rejects any version > N at publish time and during effective config computation.

#### `sync_node_configuration_override`
| Column | Type | Notes |
|--------|------|-------|
| `id` | uniqueidentifier PK | |
| `node_id` | uniqueidentifier FK→SyncNode | |
| `setting_key` | nvarchar(200) | |
| `setting_value` | nvarchar(max) | |
| `override_source` | nvarchar(20) | Manual / Imported / API |
| `updated_by` | uniqueidentifier FK→SyncUser | |
| `updated_at` | datetime2 | |

**Constraint:** unique `(node_id, setting_key)`

#### `sync_node_configuration_history`
| Column | Type | Notes |
|--------|------|-------|
| `id` | uniqueidentifier PK | |
| `node_id` | uniqueidentifier FK→SyncNode | |
| `event_type` | nvarchar(50) | see below |
| `template_id` | uniqueidentifier | nullable |
| `template_version` | int | nullable |
| `configuration_hash` | nvarchar(64) | nullable |
| `correlation_id` | nvarchar(64) | nullable; groups rollout events |
| `actor_id` | uniqueidentifier FK→SyncUser | nullable |
| `occurred_at` | datetime2 | |
| `notes` | nvarchar(500) | nullable |

**EventType values:** `Assigned` / `Unassigned` / `Applied` / `ApplyFailed` / `RolledBack` / `DriftDetected` / `DriftCleared` / `PublishDetected`

**Idempotency rule:** consecutive heartbeats reporting the same (appliedVersion, hash, state) do NOT create duplicate history events.

#### `sync_configuration_rollout`
| Column | Type | Notes |
|--------|------|-------|
| `id` | uniqueidentifier PK | returned as rolloutId in 202 response |
| `status` | nvarchar(20) | Queued / InProgress / Completed / Failed |
| `template_id` | uniqueidentifier FK→ConfigurationTemplate | |
| `template_version` | int | |
| `target_node_count` | int | total nodes targeted |
| `applied_count` | int | nodes that reached Applied state |
| `failed_count` | int | nodes that failed or couldn't be assigned |
| `initiated_by` | uniqueidentifier FK→SyncUser | |
| `started_at` | datetime2 | |
| `completed_at` | datetime2 | nullable |

Rollouts are persistent (not in-memory) so `GET /rollout/{id}` is durable across restarts. Individual assignment events in `sync_node_configuration_history` carry the `correlation_id` = rolloutId for grouping.

### SyncNode additions (M023)
| Column | Type | Notes |
|--------|------|-------|
| `assigned_template_id` | uniqueidentifier FK | nullable |
| `assigned_template_version` | int | nullable |
| `applied_template_version` | int | nullable — node reports via heartbeat |
| `expected_effective_hash` | nvarchar(64) | nullable — hub recomputes on assignment/override change |
| `applied_effective_hash` | nvarchar(64) | nullable — node reports via heartbeat |
| `configuration_state` | nvarchar(20) | nullable — computed by hub on heartbeat |
| `configuration_status_reported_at` | datetime2 | nullable — when node last reported |
| `last_applied_at` | datetime2 | nullable |

All new columns nullable — zero impact on existing rows; existing nodes get `ConfigurationState = None`.

### ConfigurationSettings value object (not a table)
```csharp
public sealed record ConfigurationSettings
{
    public int HeartbeatIntervalSeconds { get; init; }
    public string TransportMode { get; init; }           // Push / Pull / Both
    public int MaxRetryAttempts { get; init; }
    public int RetryBackoffSeconds { get; init; }
    public int BatchSizeLimit { get; init; }
    public Dictionary<string, bool> FeatureFlags { get; init; }
    public List<Guid> ChannelIds { get; init; }
    public List<Guid> RouterIds { get; init; }
    public List<Guid> TriggerIds { get; init; }
}
```

Stored as canonical JSON in `settings_json`. Channel/Router/Trigger referenced by immutable ID, not name. UI resolves IDs to display names.

**DB connection fields (DbServer, DbName, DbUser, DbPasswordEncrypted) stay on SyncNode — they are node identity, not template configuration.**

### ConfigurationState enum
```csharp
public enum ConfigurationState
{
    None,             // no template assigned
    Current,          // assigned version == applied version AND hashes match
    UpdateAvailable,  // assigned version != applied version
    Applying,         // node reported Applying status
    Drifted,          // same version but hash mismatch (local modification)
    Failed,           // node reported ApplyFailed
    Unknown           // ConfigurationStatusReportedAt older than stale threshold
}
```

**Unknown stale threshold formula:** `HeartbeatIntervalSeconds × MissedThreshold × 2` seconds, where both values come from the `Heartbeat:` config section. Default: `30 × 3 × 2 = 180 seconds`. Evaluated at query time (drift endpoint, summary, node detail) — not stored on SyncNode.

### ConfigurationAssignment domain concept
In CE, represented as columns on SyncNode (`AssignedTemplateId`, `AssignedTemplateVersion`). Conceptually a distinct domain object with fields: `AssignedTemplateId`, `AssignedTemplateVersion`, `AssignedBy`, `AssignedAt`. Extension point for Enterprise (separate table with rollout metadata).

---

## Section 2: Template Lifecycle + Validation Gate

### Lifecycle transitions
```
Draft → Published → Archived
```
No reverse transitions. Archived templates are never reactivated — clone to create a new Draft.

**Status semantics:**
- `Draft` — template has never been published; single draft version row (IsDraft=true)
- `Published` — has ≥1 published versions; may simultaneously have a new draft for the next version
- `Archived` — blocked from new assignments; blocked if any node has this as AssignedTemplateId or if a rollout is in progress

### Operations

**CreateTemplate:** creates header (Status=Draft) + draft version (IsDraft=true, VersionNumber=1, SchemaVersion=1)

**UpdateDraft:** updates `SettingsJson` on the draft version row (mutable while IsDraft=true); RowVersion in request body → 409 on concurrent edit

**CreateNewDraft:** allowed on Published templates; creates draft row (VersionNumber=CurrentPublishedVersion+1); pre-populated from latest published version's SettingsJson

**Validate (preview):** `POST /templates/{id}/validate` — runs validation gate, returns errors + canonical hash preview + effective config preview; does NOT publish

**Publish (transaction):**
1. Run validation gate
2. If fails → 422 ValidationProblemDetails with field-level errors; draft remains
3. If passes → single transaction:
   - Compute canonical JSON + ContentHash + SchemaVersion
   - Set IsDraft=false, PublishedAt, PublishedBy on version row
   - Update template header: CurrentPublishedVersion++, LatestDraftVersion=null, Status=Published
4. Audit event: `CONFIG_TEMPLATE_PUBLISHED`

**Clone:** creates new template in Draft state with SettingsJson copied from source's latest published version. Audit: `CONFIG_TEMPLATE_CLONED`

**Archive:** blocked if template has any assigned nodes or active rollouts. Status→Archived. Audit: `CONFIG_TEMPLATE_ARCHIVED`

### Validation gate (on Publish and on Validate preview)
1. All `ChannelIds` exist in DB
2. All `RouterIds` exist in DB
3. All `TriggerIds` exist in DB
4. All Channel/Router/Trigger IDs reference enabled entities
5. No duplicate IDs in ChannelIds, RouterIds, TriggerIds
6. `HeartbeatIntervalSeconds` > 0 and ≤ 3600
7. `MaxRetryAttempts` ≥ 0 and ≤ 20
8. `BatchSizeLimit` > 0 and ≤ 10,000
9. No duplicate keys in FeatureFlags
10. All FeatureFlag keys exist in a supported catalog
11. `TransportMode` is a valid enum value (Push / Pull / Both)
12. `SchemaVersion` ≤ server's supported max schema version

### Canonical hash contract
```
ContentHash = SHA256(
  UTF-8 encode(
    JSON with:
      - property names sorted lexicographically
      - FeatureFlags keys sorted (order-invariant dictionary)
      - ChannelIds, RouterIds, TriggerIds sorted by Guid string (order-invariant sets)
      - no whitespace
  )
)
```

Order-sensitive arrays (if any added in future) must NOT be sorted. This contract is documented explicitly in `CanonicalJsonSerializer` and tested independently.

### Audit events
- `CONFIG_TEMPLATE_CREATED`
- `CONFIG_TEMPLATE_DRAFT_UPDATED`
- `CONFIG_TEMPLATE_PUBLISHED`
- `CONFIG_TEMPLATE_ARCHIVED`
- `CONFIG_TEMPLATE_CLONED`

---

## Section 3: Delivery Pipeline

### Heartbeat changes

**Request body additions:**
```json
{
  "appliedTemplateVersion": 3,
  "appliedEffectiveHash": "abc123...",
  "configurationApplyStatus": "Applied"
}
```
`configurationApplyStatus` enum: `None` / `Applying` / `Applied` / `Failed`

**Response changes: 204 → 200 with body:**
```json
{
  "assignedTemplateId": "guid-or-null",
  "assignedTemplateVersion": 4,
  "contentHash": "def456...",
  "configurationState": "UpdateAvailable"
}
```
All fields nullable (null = no template assigned).

### Hub heartbeat handler logic
1. Update `AppliedTemplateVersion`, `AppliedEffectiveHash`, `ConfigurationStatusReportedAt` on SyncNode
2. Compute new `ConfigurationState` (see table below)
3. If state changed → write `NodeConfigurationHistory` event (deduplicated: consecutive same-state heartbeats produce no new event)
4. If `configurationApplyStatus == Failed` → write `ApplyFailed` history event
5. If state changed → publish SignalR `ConfigurationChangedEvent` to `operators` group AND `node-{nodeId}` group

### ConfigurationState computation
| Condition | State |
|-----------|-------|
| No AssignedTemplateId | None |
| ConfigurationStatusReportedAt older than stale threshold | Unknown |
| configurationApplyStatus == Applying | Applying |
| configurationApplyStatus == Failed | Failed |
| AssignedVersion == AppliedVersion AND ExpectedEffectiveHash == AppliedEffectiveHash | Current |
| AssignedVersion == AppliedVersion AND hash mismatch | Drifted |
| AssignedVersion ≠ AppliedVersion | UpdateAvailable |

### Dual hash model
- **TemplateContentHash** (`sync_configuration_template_version.template_content_hash`) — immutable, template settings only; used for template integrity and version verification
- **ExpectedEffectiveHash** (`SyncNode.expected_effective_hash`) — hub's computed hash of template settings merged with current node overrides; recomputed by hub when assignment changes or override changes
- **AppliedEffectiveHash** (`SyncNode.applied_effective_hash`) — what the node reports it is actually running

Drift = AssignedVersion ≠ AppliedVersion OR ExpectedEffectiveHash ≠ AppliedEffectiveHash

### Node-facing configuration endpoint
`GET /api/v1/configurations/current` — authenticated with **node token** (not user JWT)

**ETag support:** `ETag: "<expectedEffectiveHash>"`. Node sends `If-None-Match` → 304 if ExpectedEffectiveHash unchanged.

**200 response:**
```json
{
  "templateId": "guid",
  "templateVersion": 4,
  "contentHash": "abc...",
  "configurationVersion": 4,
  "schemaVersion": 1,
  "effective": {
    "heartbeatIntervalSeconds": 60,
    "transportMode": "Push",
    "maxRetryAttempts": 3,
    "retryBackoffSeconds": 60,
    "batchSizeLimit": 1000,
    "featureFlags": { "enableBulkApply": true },
    "channelIds": ["guid1"],
    "routerIds": ["guid2"],
    "triggerIds": ["guid3"]
  }
}
```
**204** if no template assigned.

### Effective config computation (merge order)
1. Template version `SettingsJson` (baseline)
2. Apply node overrides from `sync_node_configuration_override` (per-key replacement)
3. Re-validate merged result (an override could create invalid effective config)
4. Compute `ExpectedEffectiveHash` = SHA256(canonical JSON of effective settings)

Note: ExpectedEffectiveHash includes override values. TemplateContentHash never does.

### SignalR event: `ConfigurationChangedEvent`
```json
{
  "nodeId": "guid",
  "templateId": "guid",
  "assignedVersion": 4,
  "configurationState": "UpdateAvailable",
  "correlationId": "guid"
}
```
Sent to: `operators` group + `node-{nodeId}` group.

---

## Section 4: API Surface

**New permission seed (M023):** `MANAGE_CONFIGURATIONS` (admin-level)

**Route naming note:** Management APIs use `/api/v1/configuration/...` (singular, management namespace). Node-facing endpoint uses `/api/v1/configurations/current` (plural, node namespace) — intentionally distinct to enable separate auth middleware and avoid route ambiguity. This is consistent with the existing pattern where `/api/v1/nodes/{nodeId}/heartbeat` (hub-perspective) differs from `/api/v1/configurations/current` (node-perspective).

### ConfigurationTemplateController (base: `/api/v1/configuration/templates`)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/` | MANAGE_CONFIGURATIONS | List templates (filter: status) |
| POST | `/` | MANAGE_CONFIGURATIONS | Create template + initial draft |
| GET | `/{id}` | MANAGE_CONFIGURATIONS | Header + version list (lazy — versions listed, not loaded) |
| PUT | `/{id}/draft` | MANAGE_CONFIGURATIONS | Update draft (RowVersion → 409) |
| POST | `/{id}/validate` | MANAGE_CONFIGURATIONS | Validate + preview hash, no publish |
| POST | `/{id}/publish` | MANAGE_CONFIGURATIONS | Validate → publish → immutable version |
| POST | `/{id}/clone` | MANAGE_CONFIGURATIONS | Clone latest published → new Draft |
| POST | `/{id}/archive` | MANAGE_CONFIGURATIONS | Archive (blocked if assigned / active rollout) |
| GET | `/{id}/versions` | MANAGE_CONFIGURATIONS | List all version headers |
| GET | `/{id}/versions/{v}` | MANAGE_CONFIGURATIONS | Full version content |
| GET | `/{id}/versions/{v}/diff/{v2}` | MANAGE_CONFIGURATIONS | Diff two versions |

### ConfigurationAssignmentController (base: `/api/v1/configuration`)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/nodes/{nodeId}/assign` | MANAGE_CONFIGURATIONS | Assign template version (pre-flight validation) |
| DELETE | `/nodes/{nodeId}/assign` | MANAGE_CONFIGURATIONS | Unassign (node → None state) |
| GET | `/nodes/{nodeId}` | MANAGE_CONFIGURATIONS | Effective config + drift + overrides + state |
| GET | `/nodes/{nodeId}/history` | MANAGE_CONFIGURATIONS | Configuration history for node |
| POST | `/nodes/{nodeId}/overrides` | MANAGE_CONFIGURATIONS | Set/update override (key + value + source); validates through same pipeline |
| DELETE | `/nodes/{nodeId}/overrides/{key}` | MANAGE_CONFIGURATIONS | Remove override; recomputes ExpectedEffectiveHash |
| POST | `/rollout` | MANAGE_CONFIGURATIONS | Bulk assign → 202 + `{ rolloutId, status: "Queued" }` |
| GET | `/rollout/{rolloutId}` | MANAGE_CONFIGURATIONS | Rollout progress (per-node state breakdown) |
| GET | `/drift` | MANAGE_CONFIGURATIONS | All nodes + ConfigurationState. Query params: `?state=Drifted&templateId=X&version=3&nodeGroup=hub-group&search=node*` (search matches node name prefix). Filters combined with AND logic. |
| GET | `/summary` | VIEW_TOPOLOGY or MANAGE_CONFIGURATIONS | Counts by ConfigurationState |

**Assignment pre-flight validation:**
- Template status = Published
- Requested version exists and is not draft
- Template is not Archived
- Node LifecycleState ≠ Decommissioned / Decommissioning
- Node supports template's SchemaVersion
- Effective config (template + current overrides) passes validation gate

### Node-facing (base: `/api/v1/configurations`)
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/current` | Node token | Effective config; ETag; 304; 204 if no template |

### Heartbeat extension (existing endpoint)
`POST /api/v1/nodes/{nodeId}/heartbeat` — request and response bodies extended per Section 3. Response changes from 204 to 200.

**New controllers:** `ConfigurationTemplateController` + `ConfigurationAssignmentController` in `MSOSync.Api`.

---

## Section 5: Frontend

**Sidebar placement:**
```
Node Management
Configuration
    Templates
    Assignments
    Drift
Administration
```

Configuration is an operational module, not administrative.

### `/configuration/templates`
- List grid: Name, Status badge (Draft/Published/Archived), CurrentPublishedVersion, UpdatedAt, actions (Edit/Publish/Clone/Archive)
- Draft indicator on Published templates: "Published v5 · Draft v6 in progress"
- Create → dialog: name + description
- Template detail: header + lazy-loaded version history table + current draft editor
- **Draft editor:** structured form (not raw JSON) for all ConfigurationSettings fields + feature flags key-value editor + channel/router/trigger multi-select pickers (resolve IDs to names)
- Validate button (calls `POST /validate`, shows errors inline) before Publish button
- **Version comparison:** `Compare v3 ⇄ v4` using `ConfigurationDiffViewer`
- Actions via `ActionMenu` following existing pattern

### `/configuration/assignments`
- Columns: Node, NodeGroup, AssignedTemplate, AssignedVersion, AppliedVersion, ConfigurationStateBadge, LastReportedAt
- Inline version detail: "Assigned v5 / Applied v4" per row
- Row action: Assign (template picker + version select + impact preview), Unassign
- Bulk select → Rollout button → preview dialog (140 nodes / current v4 → target v5 / validation: Passed) → confirm
- Async rollout status panel: polls `GET /rollout/{id}` until terminal state (completed/failed)

### `/configuration/drift`
- Filtered view: assignments where ConfigurationState ≠ Current AND ≠ None
- Same columns as assignments + ExpectedEffectiveHash / AppliedEffectiveHash for Drifted rows
- Filter chips: by state, template, version, node group, **drift cause** (Version / Hash / Failed Apply / Unknown)
- Quick rollback action: reassign to previous version with confirm dialog

### Existing Nodes page changes
- Add `ConfigurationStateBadge` column
- Node detail panel gains **Configuration tab** with four sections:
  1. **Assignment** — template, version, state, last reported
  2. **Overrides** — key/value table with source (Manual/Imported/API) + add/remove/edit
  3. **Effective Configuration** — flat list with source indicator per field (Template / Override)
  4. **History** — timeline of configuration events

### Shared components
- **`ConfigurationStateBadge`** — 6 states; icon + text + tooltip; never colour-only
  - ✅ Current · ⏳ Update Available · ⟳ Applying · ⚠️ Drifted · ❌ Failed · ○ None / Unknown
- **`ConfigurationDiffViewer`** — added/removed/modified diff; reused in version comparison, rollback preview, assignment preview, drift investigation
- **`TemplateVersionSelect`** — published version picker for a given template
- **`EffectiveConfigPreview`** — read-only merged config with per-field source indicator

### API hooks (TanStack Query)
- `useTemplates(filters?)`, `useTemplate(id)`, `useTemplateVersions(id)`, `useTemplateVersion(id, v)`
- `useNodeConfiguration(nodeId)`, `useNodeConfigurationHistory(nodeId)`
- `useDriftSummary(filters)`, `useConfigurationSummary()`, `useRolloutStatus(rolloutId)`
- Mutations: `useCreateTemplate`, `useUpdateDraft`, `usePublishTemplate`, `useAssignTemplate`, `useRollout`, `useSetOverride`, `useRemoveOverride`

### SignalR integration
`ConfigurationChangedEvent` → eventRouter routes by category:
- Invalidate: `useNodeConfiguration(nodeId)` + `useNodeConfiguration` (list) + `useDriftSummary(filters)` + `useConfigurationSummary()`
- Does NOT invalidate `useTemplates`, `useTemplate`, `useTemplateVersions`, or unrelated query groups

Template versions: lazy-loaded (list on initial page; load content only on expand or compare action).

---

## Section 6: Testing Strategy

### Backend unit tests (SQLite EF Core, new project: MSOSync.ConfigurationTests)
New project added to solution, follows the same pattern as MSOSync.MetadataTests (SQLite, xUnit, FluentAssertions, Moq). No Testcontainers dependency — unit tests only.

**`CanonicalJsonSerializer` tests (independent):**
- Property ordering deterministic
- Order-invariant arrays (ChannelIds, RouterIds, TriggerIds, FeatureFlags keys) produce identical hash regardless of input order
- Order-sensitive arrays preserve order
- Null value handling
- UTF-8 encoding consistent
- Same input → same hash across repeated calls

**`ConfigurationTemplateService` tests:**
- Create/update-draft/publish/archive/clone lifecycle
- Publish blocked if validation fails (each rule independently)
- Archive blocked if template is assigned
- Filtered unique index: creating second draft on same template → error
- RowVersion conflict on concurrent draft edit → 409

**`EffectiveConfigurationComputer` tests:**
- Template-only settings (no overrides)
- Single override replaces template value
- Multiple overrides; each key independent
- Override removal reverts to template value
- Override does NOT affect TemplateContentHash
- ExpectedEffectiveHash changes when override changes

**`ConfigurationValidationService` tests:**
- Each of 12 validation rules triggers independently
- Referenced entity missing → specific field error
- Referenced entity disabled → specific field error
- Schema version > max supported → rejected

**`DriftDetector` tests:**
- All 6 ConfigurationState transitions
- Unknown triggered at stale threshold boundary

**`ConfigurationAssignmentService` tests:**
- Happy path assign (updates AssignedTemplateId, AssignedTemplateVersion, recomputes ExpectedEffectiveHash)
- Pre-flight blocks: archived template, decommissioned node, schema incompatible
- Unassign → ConfigurationState = None
- Rollout: bulk assign N nodes, all get history event with shared CorrelationId
- Override add/remove triggers ExpectedEffectiveHash recomputation

### Backend integration tests (LocalDB, MSOSync.IntegrationTests)

**Migration:**
- M023 applies cleanly from M022
- M023 rollback clean
- Existing SyncNodes post-migration: ConfigurationState = None, all new columns null
- Foreign keys resolve, filtered unique index exists, new indexes exist

**Full publish → assign → deploy cycle:**
- Create draft → publish → assign to node
- Heartbeat with old appliedVersion → hub sets ConfigurationState = UpdateAvailable
- GET /configurations/current → 200 with effective settings
- ETag returned; second GET with If-None-Match → 304
- Heartbeat with new version + matching hash → ConfigurationState = Current
- No duplicate history event on repeated same-state heartbeat

**Drift scenarios:**
- Same version, hash mismatch → ConfigurationState = Drifted, DriftDetected history event
- Subsequent heartbeat with corrected hash → DriftCleared history event
- ApplyFailed in heartbeat body → ApplyFailed history event, ConfigurationState = Failed

**Rollback cycle:**
- Publish v1 → assign → node reports Current → publish v2 → assign → node reports Current → rollback to v1 (assign v1) → node reports UpdateAvailable → heartbeat v1 + hash → Current

**Override hash cycle:**
- Assign template → node reports Current
- Add override → ExpectedEffectiveHash changes on hub
- Node sends old hash in heartbeat → ConfigurationState = Drifted
- Node fetches /configurations/current → applies → sends new hash → Current

**Concurrency:**
- Two operators assign different templates to same node simultaneously → one 200, one 409
- Rollout racing with manual assignment → one wins, node state deterministic

**Authorization:**
- User JWT cannot call GET /configurations/current (node-token-only endpoint)
- Node token cannot call authoring APIs
- Expired node token rejected on /configurations/current
- Node token for node A cannot retrieve node B's configuration
- MANAGE_CONFIGURATIONS required on all management endpoints; 403 with wrong permission

**Performance regression checks:**
- Rollout to 100 nodes completes within 10s
- Drift endpoint with 1000 nodes and server-side filter responds within 2s
- Template list does not load version content (lazy verified)

### Frontend tests (Vitest + MSW)

**Components:**
- `ConfigurationStateBadge` renders all 6 states with both icon and text (not colour-only)
- `ConfigurationDiffViewer` shows added/removed/modified correctly from two settings objects
- `EffectiveConfigPreview` shows source indicator (Template vs Override) per field

**Hooks:**
- `usePublishTemplate` mutation: 200 success path, 422 validation error path (field errors surfaced)
- `useAssignTemplate` mutation: optimistic update, rollback on error
- `useDriftSummary` returns counts by state
- `useRolloutStatus` polls until terminal state

**SignalR:**
- `ConfigurationChangedEvent` invalidates assignments + nodeConfiguration + drift + summary queries
- Does NOT invalidate template authoring or unrelated groups
- Duplicate events do not produce duplicate UI state changes
- Reconnect followed by config event results in correct cache state

### QA acceptance checklist
- ✅ `admin / Admin123!` logs in
- ✅ JWT issued with ADMIN role
- ✅ Dashboard loads
- ✅ SignalR connects (green indicator)
- ✅ Create draft template → form validates
- ✅ Publish template → immutable version created
- ✅ Assign template to node → ConfigurationState = UpdateAvailable
- ✅ GET /configurations/current returns effective settings
- ✅ Node reports Applied → ConfigurationState = Current
- ✅ Add node override → ExpectedEffectiveHash changes → Drifted on old hash
- ✅ Remove override → hash restores → Current on next report
- ✅ Rollback (assign v1) → UpdateAvailable → Current
- ✅ Archive blocked while assigned
- ✅ Clone template → new Draft
- ✅ Version comparison (ConfigurationDiffViewer)
- ✅ Bulk rollout → 202 + progress tracking
- ✅ Drift page filters by state and drift cause
- ✅ SignalR updates assignments grid without page refresh
- ✅ Dashboard summary counts update in real time
- ✅ Full solution build: 0 warnings
- ✅ All unit and integration tests pass
- ✅ Frontend: 0 TypeScript errors

### Definition of Done
- ✅ Immutable template versioning with Draft / Published / Archived lifecycle
- ✅ Exactly one draft per template (filtered unique index)
- ✅ Validation gate enforced at publish and preview
- ✅ Dual hash model (TemplateContentHash + ExpectedEffectiveHash + AppliedEffectiveHash)
- ✅ Drift detection operational for all 6 ConfigurationState values
- ✅ Configuration assignment with pre-flight validation
- ✅ Async rollout with progress tracking
- ✅ Rollback via reassignment (no special rollback logic)
- ✅ Node override support with per-key source tracking
- ✅ Heartbeat extended (200 response, AppliedEffectiveHash reporting)
- ✅ GET /configurations/current with ETag / 304 support
- ✅ Frontend: Templates / Assignments / Drift pages complete
- ✅ SignalR ConfigurationChangedEvent integrated
- ✅ ConfigurationStateBadge accessible (icon + text, not colour-only)
- ✅ MANAGE_CONFIGURATIONS permission seeded and enforced
- ✅ All tests green, build 0 warnings, frontend 0 TS errors
- ✅ M023 migration applies cleanly from M022

---

## Technical Constraints

- C# 13 / .NET 9 / ASP.NET Core
- `TreatWarningsAsErrors = true`
- EF Core 9.0.0 — no raw SQL where EF can handle it
- **No new NuGet packages**
- React 19 + TanStack Query v5 + Vitest
- **No new npm packages** unless already in package.json
- Unit tests: SQLite (not EF InMemory)
- Integration tests: LocalDB (existing connection string in appsettings.Development.json)
- Migration: M023 (M022 is the current latest)
- Permission seed in M023 migration
