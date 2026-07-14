import type { SyncDirection, InitialLoadPolicy } from '../../../shared/types';

export type NodeType = 'source' | 'target';

export interface ProvisionWizardDraft {
  step:               number;
  nodeType?:          NodeType;
  description?:       string;
  dbServer?:          string;
  dbName?:            string;
  nodeName?:          string;
  externalId?:        string;
  groupId?:           string;
  // Sync Scope (Step 4)
  channelIds?:        string[];
  triggerIds?:        string[];
  routerIds?:         string[];
  syncDirection?:     SyncDirection;
  initialLoadPolicy?: InitialLoadPolicy;
}

export interface ProvisionRequest {
  nodeName:     string;
  externalId:   string;
  nodeType:     NodeType;
  dbServer:     string;
  dbName:       string;
  groupId?:     string;
  description?: string;
}

export interface ProvisionResult {
  nodeId: string;
  token:  string;
}

export interface ProvisionPackageRequest {
  nodeId: string;
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
const WIZARD_VERSION     = 2 as const;

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
