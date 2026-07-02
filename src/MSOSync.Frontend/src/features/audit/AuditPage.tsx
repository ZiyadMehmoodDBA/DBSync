import { useState } from 'react';
import type { AuditFilter } from '../../shared/types';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '../../components/ui/tabs';
import { AuditFilters } from './AuditFilters';
import { AuditGrid } from './AuditGrid';
import { AuditInsightsTab } from './AuditInsightsTab';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useAuditLog } from './hooks';

const defaultFilter: AuditFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function AuditPage() {
  const [filter, setFilter] = useState<AuditFilter>(defaultFilter);
  const { data } = useAuditLog(filter); // cache-shared with AuditGrid

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Audit</h1>
      <Tabs defaultValue="log">
        <TabsList>
          <TabsTrigger value="log">Log</TabsTrigger>
          <TabsTrigger value="insights">Insights</TabsTrigger>
        </TabsList>
        <TabsContent value="log">
          <div className="flex flex-col gap-4">
            <div className="flex items-center justify-between pt-2">
              <AuditFilters onFilter={setFilter} />
              <ExportMenu
                resource="audit"
                currentData={(data?.data ?? []) as unknown as Record<string, unknown>[]}
                queryParams={
                  filter as unknown as Record<string, string | number | boolean | undefined>
                }
              />
            </div>
            <AuditGrid filter={filter} onFilterChange={setFilter} />
          </div>
        </TabsContent>
        <TabsContent value="insights">
          <AuditInsightsTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}
