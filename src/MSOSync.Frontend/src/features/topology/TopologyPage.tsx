import { TopologySummaryCards } from './TopologySummaryCards';
import { TopologyGroupsGrid } from './TopologyGroupsGrid';

export function TopologyPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Topology</h1>
        <p className="text-sm text-neutral-500 dark:text-neutral-400 mt-1">
          React Flow graph view available in Epic 10C.
        </p>
      </div>
      <TopologySummaryCards />
      <div>
        <h2 className="text-base font-semibold mb-3">Node Groups</h2>
        <TopologyGroupsGrid />
      </div>
    </div>
  );
}
