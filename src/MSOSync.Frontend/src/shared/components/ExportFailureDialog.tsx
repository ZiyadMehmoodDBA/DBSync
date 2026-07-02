import { Button } from '../../components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '../../components/ui/dialog';

interface ExportFailureDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onRetry: () => void;
  onExportCurrentView: () => void;
}

export function ExportFailureDialog({
  open,
  onOpenChange,
  onRetry,
  onExportCurrentView,
}: ExportFailureDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Export failed</DialogTitle>
          <DialogDescription>
            The full export could not be completed. You can retry, or download
            only the current view instead.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onExportCurrentView}>
            Export Current View
          </Button>
          <Button onClick={onRetry}>Retry</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
