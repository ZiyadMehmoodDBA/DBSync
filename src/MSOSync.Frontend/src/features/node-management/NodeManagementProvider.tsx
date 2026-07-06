import { createContext, useContext, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import type { TabId } from './types/tabs';
import type { RegistrationSummaryDto } from './types/registration';
import type { ProvisionWizardDraft } from './types/provision';
import { clearWizardDraft } from './types/provision';
import { NODE_MANAGEMENT_TABS } from './types/tabs';

interface NodeManagementContextValue {
  activeTab:               TabId;
  setActiveTab:            (tab: TabId) => void;
  selectedRegistration:    RegistrationSummaryDto | null;
  setSelectedRegistration: (r: RegistrationSummaryDto | null) => void;
  bulkSelection:           Set<number>;
  toggleBulkSelection:     (id: number) => void;
  clearBulkSelection:      () => void;
  wizardDraft:             ProvisionWizardDraft | null;
  setWizardDraft:          (d: ProvisionWizardDraft | null) => void;
}

export const NodeManagementContext = createContext<NodeManagementContextValue | null>(null);

export function NodeManagementProvider({
  children,
}: {
  children: ReactNode;
}) {
  const [activeTab, setActiveTab] =
    useState<TabId>(NODE_MANAGEMENT_TABS.OVERVIEW);
  const [selectedRegistration, setSelectedRegistration] =
    useState<RegistrationSummaryDto | null>(null);
  const [bulkSelection, setBulkSelection] = useState<Set<number>>(new Set());
  const [wizardDraft, setWizardDraft] =
    useState<ProvisionWizardDraft | null>(null);

  // Track previous tab so we can detect a transition away from 'provision'.
  // useRef avoids triggering re-renders and is immune to StrictMode double-mount.
  const prevTabRef = useRef<TabId | null>(null);

  useEffect(() => {
    const prev = prevTabRef.current;
    prevTabRef.current = activeTab;

    // Only clear on an actual transition away from provision (not on mount/first render).
    if (prev === NODE_MANAGEMENT_TABS.PROVISION && activeTab !== NODE_MANAGEMENT_TABS.PROVISION) {
      clearWizardDraft();
      setWizardDraft(null);
    }
  }, [activeTab]);

  // Unmount cleanup: clears draft when leaving /node-management entirely.
  useEffect(() => {
    return () => {
      clearWizardDraft();
    };
  }, []);

  function toggleBulkSelection(id: number) {
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
        toggleBulkSelection,
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
