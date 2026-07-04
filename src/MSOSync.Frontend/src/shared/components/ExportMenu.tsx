import { Download } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '../../components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../../components/ui/dropdown-menu';
import { ExportFailureDialog } from './ExportFailureDialog';
import { useExport, type ExportScope } from '../hooks/useExport';
import type { ExportResource, ExportFormat } from '../api/export';
import { useCreateExportJobMutation } from '../hooks/useExportJobs';

interface ExportMenuProps {
  resource: ExportResource;
  currentData: Record<string, unknown>[];
  queryParams: Record<string, string | number | boolean | undefined>;
  supportsAllRows?: boolean;
  canExport?: boolean;
}

export function ExportMenu({
  resource,
  currentData,
  queryParams,
  supportsAllRows = true,
  canExport = true,
}: ExportMenuProps) {
  const {
    isExporting,
    showFailureDialog,
    onExport,
    onRetry,
    onCloseFailureDialog,
    onExportCurrentViewFallback,
  } = useExport({ resource, currentData, queryParams });

  const navigate = useNavigate();
  const { mutate: createJob, isPending: isCreatingJob } = useCreateExportJobMutation();

  function handleQueueExport(format: 'csv' | 'json') {
    createJob(
      {
        resourceType: resource,
        format,
        filtersJson:  JSON.stringify(queryParams),
      },
      {
        onSuccess: () => {
          toast.success('Export queued', {
            description: 'Your download will be ready shortly.',
            action: { label: 'View Downloads', onClick: () => navigate('/downloads') },
          });
        },
        onError: () => toast.error('Failed to queue export'),
      }
    );
  }

  if (!canExport) {
    return (
      <span title="You don't have permission to export data">
        <Button variant="outline" size="sm" disabled>
          <Download className="mr-2 h-4 w-4" />
          Export
        </Button>
      </span>
    );
  }

  const handle = (scope: ExportScope, format: ExportFormat) => () =>
    onExport(scope, format);

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" disabled={isExporting || isCreatingJob}>
            <Download className="mr-2 h-4 w-4" />
            {isExporting ? 'Exporting…' : 'Export'}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-48">
          <DropdownMenuLabel>Current View</DropdownMenuLabel>
          <DropdownMenuGroup>
            <DropdownMenuItem onClick={handle('view', 'csv')}>CSV</DropdownMenuItem>
            <DropdownMenuItem onClick={handle('view', 'json')}>JSON</DropdownMenuItem>
          </DropdownMenuGroup>
          {supportsAllRows && (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuLabel>All Matching (Background)</DropdownMenuLabel>
              <DropdownMenuGroup>
                <DropdownMenuItem onClick={() => handleQueueExport('csv')} disabled={isCreatingJob}>
                  {isCreatingJob ? 'Queuing…' : 'CSV'}
                </DropdownMenuItem>
                <DropdownMenuItem onClick={() => handleQueueExport('json')} disabled={isCreatingJob}>
                  {isCreatingJob ? 'Queuing…' : 'JSON'}
                </DropdownMenuItem>
              </DropdownMenuGroup>
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <ExportFailureDialog
        open={showFailureDialog}
        onOpenChange={onCloseFailureDialog}
        onRetry={onRetry}
        onExportCurrentView={onExportCurrentViewFallback}
      />
    </>
  );
}
