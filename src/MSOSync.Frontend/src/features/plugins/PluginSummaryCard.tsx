import { Package } from 'lucide-react';
import { SummaryCard } from '../../shared/components/data-display/SummaryCard';
import { usePluginSummary } from './hooks';

export function PluginSummaryCard() {
  const { data, isLoading } = usePluginSummary();

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <SummaryCard
        title="Plugins Loaded"
        value={data?.loaded ?? 0}
        subtitle={`of ${data?.total ?? 0} total`}
        icon={Package}
        variant={data && data.failed > 0 ? 'warning' : 'success'}
        loading={isLoading}
      />
      <SummaryCard
        title="Failed"
        value={data?.failed ?? 0}
        icon={Package}
        variant={data && data.failed > 0 ? 'danger' : 'default'}
        loading={isLoading}
      />
      <SummaryCard
        title="Disabled"
        value={data?.disabled ?? 0}
        icon={Package}
        loading={isLoading}
      />
      <SummaryCard
        title="Startup"
        value={data ? `${data.startupDurationMs}ms` : '—'}
        icon={Package}
        loading={isLoading}
      />
    </div>
  );
}
