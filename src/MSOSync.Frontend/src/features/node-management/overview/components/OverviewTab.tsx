import { useNodeManagementOverview } from '../../hooks/useNodeManagementOverview';
import { StatCard } from './StatCard';

export function OverviewTab() {
  const { data, isLoading, isError } = useNodeManagementOverview();

  if (isLoading) return <div className="p-6 text-sm text-neutral-400">Loading…</div>;
  if (isError)   return <div className="p-6 text-sm text-red-500">Failed to load overview.</div>;
  if (!data)     return null;

  return (
    <div className="p-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4 mb-6">
        <StatCard
          label="Pending Registrations"
          value={data.pendingRegistrations}
          description="Awaiting approval"
        />
        <StatCard
          label="Pending Recoveries"
          value={data.pendingRecoveries}
          description="Recovery requests"
        />
        <StatCard label="Total Nodes"   value={data.totalNodes} />
        <StatCard label="Active Nodes"  value={data.activeNodes} />
        <StatCard label="Offline Nodes" value={data.offlineNodes} />
        <StatCard label="Degraded"      value={data.degradedNodes} />
        <StatCard label="Total Groups"  value={data.totalGroups} />
      </div>
      <p className="text-xs text-neutral-400">
        Generated at {new Date(data.generatedAt).toLocaleString()}
      </p>
    </div>
  );
}
