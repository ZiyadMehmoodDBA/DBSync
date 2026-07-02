import { useState, useCallback } from 'react';
import {
  downloadExport,
  downloadCurrentViewCsv,
  downloadCurrentViewJson,
  type ExportResource,
  type ExportFormat,
} from '../api/export';

export type ExportScope = 'view' | 'all';

interface UseExportOptions {
  resource: ExportResource;
  currentData: Record<string, unknown>[];
  queryParams: Record<string, string | number | boolean | undefined>;
}

interface UseExportReturn {
  isExporting: boolean;
  showFailureDialog: boolean;
  pendingViewFormat: ExportFormat | null;
  onExport: (scope: ExportScope, format: ExportFormat) => void;
  onRetry: () => void;
  onCloseFailureDialog: () => void;
  onExportCurrentViewFallback: () => void;
}

export function useExport({
  resource,
  currentData,
  queryParams,
}: UseExportOptions): UseExportReturn {
  const [isExporting, setIsExporting] = useState(false);
  const [showFailureDialog, setShowFailureDialog] = useState(false);
  const [lastAllRowsFormat, setLastAllRowsFormat] = useState<ExportFormat>('csv');
  const [pendingViewFormat, setPendingViewFormat] = useState<ExportFormat | null>(null);

  const runAllRowsExport = useCallback(
    async (format: ExportFormat) => {
      setLastAllRowsFormat(format);
      setIsExporting(true);
      try {
        await downloadExport(resource, format, queryParams);
      } catch {
        setPendingViewFormat(format);
        setShowFailureDialog(true);
      } finally {
        setIsExporting(false);
      }
    },
    [resource, queryParams],
  );

  const onExport = useCallback(
    (scope: ExportScope, format: ExportFormat) => {
      if (scope === 'view') {
        if (format === 'csv') downloadCurrentViewCsv(currentData, resource);
        else downloadCurrentViewJson(currentData, resource);
      } else {
        void runAllRowsExport(format);
      }
    },
    [currentData, resource, runAllRowsExport],
  );

  const onRetry = useCallback(() => {
    setShowFailureDialog(false);
    void runAllRowsExport(lastAllRowsFormat);
  }, [lastAllRowsFormat, runAllRowsExport]);

  const onCloseFailureDialog = useCallback(() => {
    setShowFailureDialog(false);
    setPendingViewFormat(null);
  }, []);

  const onExportCurrentViewFallback = useCallback(() => {
    if (!pendingViewFormat) return;
    setShowFailureDialog(false);
    if (pendingViewFormat === 'csv') downloadCurrentViewCsv(currentData, resource);
    else downloadCurrentViewJson(currentData, resource);
    setPendingViewFormat(null);
  }, [pendingViewFormat, currentData, resource]);

  return {
    isExporting,
    showFailureDialog,
    pendingViewFormat,
    onExport,
    onRetry,
    onCloseFailureDialog,
    onExportCurrentViewFallback,
  };
}
