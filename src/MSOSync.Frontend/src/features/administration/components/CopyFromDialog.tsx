import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '../../../components/ui/dialog';
import { Button } from '../../../components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../../components/ui/select';

interface Props {
  open: boolean;
  targetRoleName: string;
  allRoleNames: string[];
  isPending: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: (sourceRole: string) => void;
}

export function CopyFromDialog({
  open,
  targetRoleName,
  allRoleNames,
  isPending,
  onOpenChange,
  onConfirm,
}: Props) {
  const [sourceRole, setSourceRole] = useState('');
  const otherRoles = allRoleNames.filter(r => r !== targetRoleName);

  const handleConfirm = () => {
    if (!sourceRole) return;
    onConfirm(sourceRole);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Copy Permissions From</DialogTitle>
        </DialogHeader>
        <div className="py-2">
          <p className="text-sm text-neutral-500 mb-3">
            Replace <strong>{targetRoleName}</strong>'s permissions with those from:
          </p>
          <Select value={sourceRole} onValueChange={setSourceRole}>
            <SelectTrigger>
              <SelectValue placeholder="Select a role…" />
            </SelectTrigger>
            <SelectContent>
              {otherRoles.map(r => (
                <SelectItem key={r} value={r}>{r}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={handleConfirm} disabled={!sourceRole || isPending}>
            {isPending ? 'Copying…' : 'Copy Permissions'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
