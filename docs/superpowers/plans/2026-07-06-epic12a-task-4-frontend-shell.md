# Epic 12A Task 4: Frontend Shell + Routing

> **For agentic workers:** This is Task 4 of 7. Tasks 1–3 must be complete (backend fully green). This task is pure frontend — no C# changes.

**Goal:** Scaffold the `node-management` feature folder, wire the router and sidebar, create `NodeManagementPage` with lazy tab strip, `NodeManagementProvider` with React context, and stub tab components for all 5 tabs.

## Global Constraints

- React 19, TanStack Query v5 — no new npm packages
- Lucide icons only (already installed)
- shadcn/ui components only (already installed: `Button`, `Tabs`, `Separator`, etc.)
- `NODE_MANAGEMENT_TABS` as const — exact values: `overview`, `registrations`, `provision`, `nodes`, `groups`
- Wizard draft sessionStorage key: `"msosync:wizard:provision"` with envelope `{ "version": 1, "draft": {...} }`
- `/nodes` → 302 redirect → `/node-management` in router
- TypeScript strict mode: no `any`, no unused vars
- Run `npm run build` to verify no TS errors (do not rely on test output alone for TS correctness)

## Files

**Create:**
- `src/MSOSync.Frontend/src/features/node-management/types/tabs.ts`
- `src/MSOSync.Frontend/src/features/node-management/types/registration.ts`
- `src/MSOSync.Frontend/src/features/node-management/types/provision.ts`
- `src/MSOSync.Frontend/src/features/node-management/NodeManagementProvider.tsx`
- `src/MSOSync.Frontend/src/features/node-management/NodeManagementPage.tsx`
- `src/MSOSync.Frontend/src/features/node-management/overview/components/OverviewTab.tsx` (stub)
- `src/MSOSync.Frontend/src/features/node-management/overview/components/StatCard.tsx`
- `src/MSOSync.Frontend/src/features/node-management/registrations/components/RegistrationsTab.tsx` (stub)
- `src/MSOSync.Frontend/src/features/node-management/provision/components/ProvisionTab.tsx` (stub)
- `src/MSOSync.Frontend/src/features/node-management/nodes/components/NodesTab.tsx` (stub)
- `src/MSOSync.Frontend/src/features/node-management/groups/components/GroupsTab.tsx` (stub)

**Modify:**
- `src/MSOSync.Frontend/src/app/router.tsx` — add `/node-management` route + `/nodes` redirect
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add "Node Management" nav item

## Interfaces Produced (used by Tasks 5 and 6)

```typescript
// NodeManagementProvider exports:
export const NodeManagementContext: React.Context<NodeManagementContextValue>
export function NodeManagementProvider({ children }: { children: React.ReactNode }): JSX.Element
export function useNodeManagement(): NodeManagementContextValue

// Types:
export type TabId = 'overview' | 'registrations' | 'provision' | 'nodes' | 'groups'
export const NODE_MANAGEMENT_TABS: { OVERVIEW: 'overview'; REGISTRATIONS: 'registrations'; PROVISION: 'provision'; NODES: 'nodes'; GROUPS: 'groups' }
```

---

## Steps

- [ ] **Step 1: Create types/tabs.ts**

```typescript
// src/MSOSync.Frontend/src/features/node-management/types/tabs.ts
export const NODE_MANAGEMENT_TABS = {
  OVERVIEW:      'overview',
  REGISTRATIONS: 'registrations',
  PROVISION:     'provision',
  NODES:         'nodes',
  GROUPS:        'groups',
} as const;

export type TabId = (typeof NODE_MANAGEMENT_TABS)[keyof typeof NODE_MANAGEMENT_TABS];
```

- [ ] **Step 2: Create types/registration.ts**

```typescript
// src/MSOSync.Frontend/src/features/node-management/types/registration.ts
export type RegistrationType  = 'New' | 'ReRegistration' | 'Recovery';
export type RegistrationStatus = 'Pending' | 'Approved' | 'Rejected';
export type RegistrationChangeType = 'Unchanged' | 'Added' | 'Modified' | 'Removed';

export interface RegistrationSummaryDto {
  id:               number;
  nodeExternalId:   string;
  nodeName:         string;
  registrationType: RegistrationType;
  status:           RegistrationStatus;
  receivedAt:       string;
  processedAt:      string | null;
  processedBy:      string | null;
}

export interface RegistrationDiffItemDto {
  field:         string;
  currentValue:  string | null;
  incomingValue: string | null;
  changeType:    RegistrationChangeType;
}

export interface RegistrationDiffDto {
  items: RegistrationDiffItemDto[];
}

export interface MachineMetadata {
  hostName?:    string;
  osVersion?:   string;
  machineName?: string;
}

export interface DatabaseMetadata {
  edition?:      string;
  version?:      string;
  collation?:    string;
  instanceName?: string;
}

export interface ApplicationMetadata {
  agentVersion?:   string;
  runtimeVersion?: string;
  installPath?:    string;
}

export interface HardwareMetadata {
  cpuCount?:  number;
  ramBytes?:  number;
  diskBytes?: number;
}

export interface RegistrationMetadataDto {
  schemaVersion: number;
  machine?:      MachineMetadata;
  database?:     DatabaseMetadata;
  application?:  ApplicationMetadata;
  hardware?:     HardwareMetadata;
}

export interface RegistrationDetailDto extends RegistrationSummaryDto {
  metadata: RegistrationMetadataDto | null;
  diff:     RegistrationDiffDto | null;
}

export interface RegistrationListFilter {
  status?:           RegistrationStatus;
  registrationType?: RegistrationType;
  pageSize?:         number;
  cursor?:           string;
  includeTotalCount?: boolean;
}

export interface CursorPageResult<T> {
  items:       T[];
  nextCursor:  string | null;
  totalCount?: number;
}
```

- [ ] **Step 3: Create types/provision.ts**

```typescript
// src/MSOSync.Frontend/src/features/node-management/types/provision.ts
export type NodeType = 'source' | 'target';

export interface ProvisionWizardDraft {
  step:        number;
  nodeType?:   NodeType;
  description?: string;
  dbServer?:   string;
  dbName?:     string;
  nodeName?:   string;
  externalId?: string;
  groupId?:    string;
}

export interface ProvisionRequest {
  nodeName:    string;
  externalId:  string;
  nodeType:    NodeType;
  dbServer:    string;
  dbName:      string;
  groupId?:    string;
  description?: string;
}

export interface ProvisionResult {
  nodeId: string;
  token:  string;
}

export interface ProvisionPackageRequest {
  nodeId: string;
  token:  string;
}

export interface NodeManagementOverviewDto {
  pendingRegistrations: number;
  pendingRecoveries:    number;
  totalNodes:           number;
  activeNodes:          number;
  offlineNodes:         number;
  degradedNodes:        number;
  totalGroups:          number;
  lastRegistrationAt:   string | null;
  lastApprovalAt:       string | null;
  generatedAt:          string;
}

const WIZARD_STORAGE_KEY = 'msosync:wizard:provision' as const;
const WIZARD_VERSION     = 1 as const;

interface WizardEnvelope {
  version: number;
  draft:   ProvisionWizardDraft;
}

export function loadWizardDraft(): ProvisionWizardDraft | null {
  try {
    const raw = sessionStorage.getItem(WIZARD_STORAGE_KEY);
    if (!raw) return null;
    const envelope = JSON.parse(raw) as WizardEnvelope;
    if (envelope.version !== WIZARD_VERSION) return null;
    return envelope.draft;
  } catch {
    return null;
  }
}

export function saveWizardDraft(draft: ProvisionWizardDraft): void {
  const envelope: WizardEnvelope = { version: WIZARD_VERSION, draft };
  sessionStorage.setItem(WIZARD_STORAGE_KEY, JSON.stringify(envelope));
}

export function clearWizardDraft(): void {
  sessionStorage.removeItem(WIZARD_STORAGE_KEY);
}
```

- [ ] **Step 4: Create NodeManagementProvider.tsx**

```tsx
// src/MSOSync.Frontend/src/features/node-management/NodeManagementProvider.tsx
import { createContext, useContext, useState } from 'react';
import type { TabId } from './types/tabs';
import type { RegistrationSummaryDto } from './types/registration';
import type { ProvisionWizardDraft } from './types/provision';
import { NODE_MANAGEMENT_TABS } from './types/tabs';

interface NodeManagementContextValue {
  activeTab:               TabId;
  setActiveTab:            (tab: TabId) => void;
  selectedRegistration:    RegistrationSummaryDto | null;
  setSelectedRegistration: (r: RegistrationSummaryDto | null) => void;
  bulkSelection:           Set<number>;
  toggleBulkSelect:        (id: number) => void;
  clearBulkSelection:      () => void;
  wizardDraft:             ProvisionWizardDraft | null;
  setWizardDraft:          (d: ProvisionWizardDraft | null) => void;
}

const NodeManagementContext = createContext<NodeManagementContextValue | null>(null);

export function NodeManagementProvider({
  children,
}: {
  children: React.ReactNode;
}) {
  const [activeTab, setActiveTab] =
    useState<TabId>(NODE_MANAGEMENT_TABS.OVERVIEW);
  const [selectedRegistration, setSelectedRegistration] =
    useState<RegistrationSummaryDto | null>(null);
  const [bulkSelection, setBulkSelection] = useState<Set<number>>(new Set());
  const [wizardDraft, setWizardDraft] =
    useState<ProvisionWizardDraft | null>(null);

  function toggleBulkSelect(id: number) {
    setBulkSelection(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function clearBulkSelection() {
    setBulkSelection(new Set());
  }

  return (
    <NodeManagementContext.Provider
      value={{
        activeTab,
        setActiveTab,
        selectedRegistration,
        setSelectedRegistration,
        bulkSelection,
        toggleBulkSelect,
        clearBulkSelection,
        wizardDraft,
        setWizardDraft,
      }}
    >
      {children}
    </NodeManagementContext.Provider>
  );
}

export function useNodeManagement(): NodeManagementContextValue {
  const ctx = useContext(NodeManagementContext);
  if (!ctx) throw new Error('useNodeManagement must be used within NodeManagementProvider');
  return ctx;
}
```

- [ ] **Step 5: Create stub tab components**

```tsx
// src/MSOSync.Frontend/src/features/node-management/overview/components/OverviewTab.tsx
export function OverviewTab() {
  return <div className="p-6">Overview — coming in Task 5</div>;
}
```

```tsx
// src/MSOSync.Frontend/src/features/node-management/overview/components/StatCard.tsx
interface StatCardProps {
  label: string;
  value: number | string;
  description?: string;
}

export function StatCard({ label, value, description }: StatCardProps) {
  return (
    <div className="rounded-lg border p-4 bg-white dark:bg-neutral-900">
      <p className="text-sm text-neutral-500 dark:text-neutral-400">{label}</p>
      <p className="text-2xl font-bold mt-1">{value}</p>
      {description && (
        <p className="text-xs text-neutral-400 mt-1">{description}</p>
      )}
    </div>
  );
}
```

```tsx
// src/MSOSync.Frontend/src/features/node-management/registrations/components/RegistrationsTab.tsx
export function RegistrationsTab() {
  return <div className="p-6">Registrations — coming in Task 5</div>;
}
```

```tsx
// src/MSOSync.Frontend/src/features/node-management/provision/components/ProvisionTab.tsx
export function ProvisionTab() {
  return <div className="p-6">Provision Wizard — coming in Task 6</div>;
}
```

```tsx
// src/MSOSync.Frontend/src/features/node-management/nodes/components/NodesTab.tsx
export function NodesTab() {
  return <div className="p-6">Nodes — read-only grid (12B)</div>;
}
```

```tsx
// src/MSOSync.Frontend/src/features/node-management/groups/components/GroupsTab.tsx
export function GroupsTab() {
  return <div className="p-6">Groups — read-only grid (12B)</div>;
}
```

- [ ] **Step 6: Create NodeManagementPage.tsx**

```tsx
// src/MSOSync.Frontend/src/features/node-management/NodeManagementPage.tsx
import { lazy, Suspense } from 'react';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';
import { NodeManagementProvider, useNodeManagement } from './NodeManagementProvider';
import { NODE_MANAGEMENT_TABS } from './types/tabs';
import type { TabId } from './types/tabs';
import { cn } from '../../lib/utils';

const OverviewTab     = lazy(() =>
  import('./overview/components/OverviewTab').then(m => ({ default: m.OverviewTab })));
const RegistrationsTab = lazy(() =>
  import('./registrations/components/RegistrationsTab').then(m => ({ default: m.RegistrationsTab })));
const ProvisionTab    = lazy(() =>
  import('./provision/components/ProvisionTab').then(m => ({ default: m.ProvisionTab })));
const NodesTab        = lazy(() =>
  import('./nodes/components/NodesTab').then(m => ({ default: m.NodesTab })));
const GroupsTab       = lazy(() =>
  import('./groups/components/GroupsTab').then(m => ({ default: m.GroupsTab })));

function TabBar() {
  const { activeTab, setActiveTab } = useNodeManagement();
  const canViewTopology = useHasPermission(PermissionKeys.ViewTopology);
  const canManageUsers  = useHasPermission(PermissionKeys.ManageUsers);

  const tabs: { id: TabId; label: string; visible: boolean }[] = [
    { id: NODE_MANAGEMENT_TABS.OVERVIEW,       label: 'Overview',      visible: canViewTopology },
    { id: NODE_MANAGEMENT_TABS.REGISTRATIONS,  label: 'Registrations', visible: canViewTopology },
    { id: NODE_MANAGEMENT_TABS.PROVISION,      label: 'Provision',     visible: canManageUsers },
    { id: NODE_MANAGEMENT_TABS.NODES,          label: 'Nodes',         visible: canViewTopology },
    { id: NODE_MANAGEMENT_TABS.GROUPS,         label: 'Groups',        visible: canViewTopology },
  ];

  return (
    <div className="flex border-b border-neutral-200 dark:border-neutral-800 px-6">
      {tabs
        .filter(t => t.visible)
        .map(t => (
          <button
            key={t.id}
            onClick={() => setActiveTab(t.id)}
            className={cn(
              'px-4 py-3 text-sm font-medium border-b-2 transition-colors',
              activeTab === t.id
                ? 'border-blue-600 text-blue-600 dark:border-blue-400 dark:text-blue-400'
                : 'border-transparent text-neutral-500 dark:text-neutral-400 hover:text-neutral-700 dark:hover:text-neutral-300',
            )}
          >
            {t.label}
          </button>
        ))}
    </div>
  );
}

function TabContent() {
  const { activeTab } = useNodeManagement();

  return (
    <Suspense fallback={<div className="p-6 text-sm text-neutral-400">Loading…</div>}>
      {activeTab === NODE_MANAGEMENT_TABS.OVERVIEW       && <OverviewTab />}
      {activeTab === NODE_MANAGEMENT_TABS.REGISTRATIONS  && <RegistrationsTab />}
      {activeTab === NODE_MANAGEMENT_TABS.PROVISION      && <ProvisionTab />}
      {activeTab === NODE_MANAGEMENT_TABS.NODES          && <NodesTab />}
      {activeTab === NODE_MANAGEMENT_TABS.GROUPS         && <GroupsTab />}
    </Suspense>
  );
}

export function NodeManagementPage() {
  return (
    <NodeManagementProvider>
      <div className="flex flex-col h-full">
        <div className="px-6 pt-6 pb-2">
          <h1 className="text-xl font-semibold">Node Management</h1>
          <p className="text-sm text-neutral-500 dark:text-neutral-400 mt-1">
            Review registrations, approve nodes, and provision new sync nodes.
          </p>
        </div>
        <TabBar />
        <div className="flex-1 overflow-y-auto">
          <TabContent />
        </div>
      </div>
    </NodeManagementProvider>
  );
}
```

- [ ] **Step 7: Update router.tsx**

Open `src/MSOSync.Frontend/src/app/router.tsx`. Add the import at the top:

```typescript
import { NodeManagementPage } from '../features/node-management/NodeManagementPage';
```

In the `children` array under `AppLayout`, replace:
```typescript
{ path: 'nodes',            element: <NodesPage /> },
```
With:
```typescript
{ path: 'nodes',            element: <Navigate to="/node-management" replace /> },
{ path: 'node-management',  element: <PermissionGuard permissionKey={PermissionKeys.ViewTopology}><NodeManagementPage /></PermissionGuard> },
```

- [ ] **Step 8: Update AppLayout.tsx sidebar**

Open `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`. In the `NAV_GROUPS` array, find the `'Topology'` group and replace:

```typescript
{ label: 'Nodes',     path: '/nodes',     icon: Server },
```

With:

```typescript
{ label: 'Node Management', path: '/node-management', icon: Server, requiredPermission: PermissionKeys.ViewTopology },
```

The `PermissionKeys.ViewTopology` already exists in the `permMap` object in `NavGroup`.

- [ ] **Step 9: Verify TypeScript build**

```pwsh
cd src/MSOSync.Frontend
npm run build
```

Expected: Build succeeds with zero TypeScript errors. Fix any TS errors before proceeding (do not use `any` to suppress them).

- [ ] **Step 10: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/features/node-management/ `
  src/MSOSync.Frontend/src/app/router.tsx `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
git commit -m "feat(12A): frontend shell — NodeManagementPage, provider, tab stubs, routing"
```
