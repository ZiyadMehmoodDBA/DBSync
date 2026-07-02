import { Download } from 'lucide-react';
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

interface ExportMenuProps {
  resource: ExportResource;
  currentData: Record<string, unknown>[];
  queryParams: Record<string, string | number | boolean | undefined>;
  supportsAllRows?: boolean;
}

export function ExportMenu({
  resource,
  currentData,
  queryParams,
  supportsAllRows = true,
}: ExportMenuProps) {
  const {
    isExporting,
    showFailureDialog,
    onExport,
    onRetry,
    onCloseFailureDialog,
    onExportCurrentViewFallback,
  } = useExport({ resource, currentData, queryParams });

  const handle = (scope: ExportScope, format: ExportFormat) => () =>
    onExport(scope, format);

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" disabled={isExporting}>
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
              <DropdownMenuLabel>All Matching Rows</DropdownMenuLabel>
              <DropdownMenuGroup>
                <DropdownMenuItem onClick={handle('all', 'csv')}>CSV</DropdownMenuItem>
                <DropdownMenuItem onClick={handle('all', 'json')}>JSON</DropdownMenuItem>
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
