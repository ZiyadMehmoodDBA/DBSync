import { useState } from 'react';
import {
  Tabs,
  TabsList,
  TabsTrigger,
  TabsContent,
} from '../../components/ui/tabs';
import { TopologySummaryCards } from './TopologySummaryCards';
import { TopologyGroupsGrid } from './TopologyGroupsGrid';
import { TopologyGraph } from './graph';

const TOPOLOGY_TABS = {
  GRAPH:  'graph',
  GROUPS: 'groups',
} as const;

type TopologyTab = typeof TOPOLOGY_TABS[keyof typeof TOPOLOGY_TABS];

export function TopologyPage() {
  const [selectedTab, setSelectedTab] = useState<TopologyTab>(TOPOLOGY_TABS.GRAPH);

  function handleViewInTable(_groupId: string) {
    setSelectedTab(TOPOLOGY_TABS.GROUPS);
    // Epic 11D/11E: pass groupId to scroll/highlight row in groups table
  }

  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Topology</h1>
        <p className="text-sm text-neutral-500 dark:text-neutral-400 mt-1">
          Network topology of node groups and router connections.
        </p>
      </div>

      {/* Summary cards are global topology state — always visible, outside tab boundary */}
      <TopologySummaryCards />

      <Tabs
        value={selectedTab}
        onValueChange={(v) => setSelectedTab(v as TopologyTab)}
      >
        <TabsList>
          <TabsTrigger value={TOPOLOGY_TABS.GRAPH}>Graph</TabsTrigger>
          <TabsTrigger value={TOPOLOGY_TABS.GROUPS}>Groups</TabsTrigger>
        </TabsList>

        <TabsContent value={TOPOLOGY_TABS.GRAPH}>
          <TopologyGraph onViewInTable={handleViewInTable} />
        </TabsContent>

        <TabsContent value={TOPOLOGY_TABS.GROUPS}>
          <div>
            <h2 className="text-base font-semibold mb-3">Node Groups</h2>
            <TopologyGroupsGrid />
          </div>
        </TabsContent>
      </Tabs>
    </div>
  );
}
