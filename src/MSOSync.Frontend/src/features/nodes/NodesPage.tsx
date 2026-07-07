import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { NodesGrid } from './NodesGrid';
import { NodeDialog } from './NodeDialog';
import { CreateNodeDialog } from './CreateNodeDialog';
import { useNodes } from './hooks';
import { ExportMenu } from '../../shared/components/ExportMenu';
import type { NodeDto } from '../../shared/types';
import { usePreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

const PAGE_SIZE = 50;

export function NodesPage() {
  const [search, setSearch] = useState('');
  const [editState, setEditState] = useState<NodeDto | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);

  const savedPageSize = usePreference<number>(PreferenceKeys.nodesPageSize, 25);

  const canExport  = useHasPermission(PermissionKeys.ExportData);
  const canManage  = useHasPermission(PermissionKeys.ManageUsers);

  const { data: nodesData, isLoading, error, refetch } = useNodes(pageNumber, PAGE_SIZE);
  const nodes = nodesData?.data;
  const totalCount = nodesData?.total ?? 0;

  const onEdit = useCallback((node: NodeDto) => {
    setEditState(node);
  }, []);

  const rangeStart = totalCount > 0 ? (pageNumber - 1) * PAGE_SIZE + 1 : 0;
  const rangeEnd = Math.min(pageNumber * PAGE_SIZE, totalCount);

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Nodes</h1>
        <ExportMenu
          resource="nodes"
          currentData={(nodes ?? []) as unknown as Record<string, unknown>[]}
          queryParams={{}}
          supportsAllRows={false}
          canExport={canExport}
        />
      </div>
      <div className="flex items-center gap-2">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search nodes…"
          className="max-w-xs"
        />
        {canManage && (
          <Button onClick={() => setCreateOpen(true)}>Add Node</Button>
        )}
      </div>
      <NodesGrid
        rowData={nodes}
        isLoading={isLoading}
        error={error}
        onRetry={() => void refetch()}
        quickFilterText={search}
        onEdit={onEdit}
        paginationPageSize={savedPageSize}
      />
      {!isLoading && totalCount > 0 && (
        <div className="flex items-center justify-between px-2 py-3 border-t text-sm text-muted-foreground">
          <span>
            Showing {rangeStart}–{rangeEnd} of {totalCount}
          </span>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPageNumber(p => p - 1)}
              disabled={pageNumber === 1}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPageNumber(p => p + 1)}
              disabled={pageNumber * PAGE_SIZE >= totalCount}
            >
              Next
            </Button>
          </div>
        </div>
      )}
      {editState && (
        <NodeDialog
          open={!!editState}
          initialValues={editState}
          onOpenChange={(open) => {
            if (!open) setEditState(null);
          }}
        />
      )}
      <CreateNodeDialog open={createOpen} onOpenChange={setCreateOpen} />
    </div>
  );
}
