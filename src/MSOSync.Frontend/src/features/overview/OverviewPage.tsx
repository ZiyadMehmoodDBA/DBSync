import { useOverview } from '@/shared/hooks/useOverview';
import { OverviewHealthBar } from './components/OverviewHealthBar';
import { OverviewActionCards } from './components/OverviewActionCards';
import { OverviewQuickActions } from './components/OverviewQuickActions';
import { OverviewActivityFeed } from './components/OverviewActivityFeed';
import { OverviewSystemInfo } from './components/OverviewSystemInfo';

export function OverviewPage() {
  const { data, isLoading, isRefetching, refetch } = useOverview();

  if (isLoading) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted-foreground">
        Loading overview...
      </div>
    );
  }

  if (!data) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-destructive">
        Failed to load overview. Check API connection.
      </div>
    );
  }

  return (
    <div className="space-y-6 p-6">
      {/* Page heading */}
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Overview</h1>
        <p className="text-sm text-muted-foreground">System health at a glance</p>
      </div>

      {/* Zone A — Health bar */}
      <OverviewHealthBar
        health={data.health}
        operations={data.operations}
        lastRefreshedAt={data.lastRefreshedAt}
        onRefresh={() => void refetch()}
        isRefreshing={isRefetching}
      />

      {/* Zone C — Quick actions */}
      <section>
        <h2 className="mb-2 text-sm font-semibold text-muted-foreground uppercase tracking-wider">
          Quick Actions
        </h2>
        <OverviewQuickActions />
      </section>

      {/* Zone B — Actionable warnings */}
      {data.warnings.length > 0 && (
        <section>
          <h2 className="mb-2 text-sm font-semibold text-muted-foreground uppercase tracking-wider">
            Requires Attention ({data.warnings.length})
          </h2>
          <OverviewActionCards warnings={data.warnings} />
        </section>
      )}

      {/* Zone D — Activity feed */}
      <section>
        <h2 className="mb-2 text-sm font-semibold text-muted-foreground uppercase tracking-wider">
          Recent Activity
        </h2>
        <OverviewActivityFeed events={data.recentActivity} />
      </section>

      {/* Zone E — System info strip */}
      <OverviewSystemInfo />
    </div>
  );
}
