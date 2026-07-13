import { SummaryCards } from './SummaryCards';
import { ActivityFeed } from './ActivityFeed';

export function DashboardPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <h1 className="text-2xl font-semibold">Dashboard</h1>
      <p className="text-sm text-muted-foreground">Executive Summary</p>
      <SummaryCards />
      <ActivityFeed />
    </div>
  );
}
