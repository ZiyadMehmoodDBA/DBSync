import { useState } from 'react';
import { MoreVertical } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { getNodeTransitions } from '../../../../shared/api/lifecycle';
import { queryKeys } from '../../../../shared/queryKeys';
import type { TransitionActionDto } from '../../../../shared/types/lifecycle';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '../../../../components/ui/dropdown-menu';
import { Button } from '../../../../components/ui/button';

const LABELS: Record<string, string> = {
  Enable: 'Enable',
  Disable: 'Disable',
  StartMaintenance: 'Start Maintenance',
  EndMaintenance: 'End Maintenance',
  StartDrain: 'Start Drain',
  ResumeDrain: 'Resume from Drain',
  Decommission: 'Decommission',
  ForceCompleteDecommission: 'Force Complete Decommission',
};

export interface NodeActionsMenuProps {
  nodeId: string;
  canManage: boolean;
  onAction: (nodeId: string, action: TransitionActionDto) => void;
}

/**
 * Renders EXACTLY what GET /transitions returns. requiresReason / requiresConfirmation /
 * dangerLevel drive the downstream dialog choice in NodesGrid — this component encodes
 * zero transition rules (spec §11.2).
 */
export function NodeActionsMenu({ nodeId, canManage, onAction }: NodeActionsMenuProps) {
  const [open, setOpen] = useState(false);
  const { data, isLoading } = useQuery({
    queryKey: queryKeys.nodeTransitions(nodeId),
    queryFn: ({ signal }) => getNodeTransitions(nodeId, { signal }),
    enabled: open,                 // lazy: fetched when the menu opens
    staleTime: 5_000,
  });

  const actions = canManage ? (data?.allowedTransitions ?? []) : [];

  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="h-7 w-7 p-0" aria-label="Node actions">
          <MoreVertical className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {isLoading && <DropdownMenuItem disabled>Loading…</DropdownMenuItem>}
        {!isLoading && actions.length === 0 && (
          <DropdownMenuItem disabled>
            {canManage ? 'No permitted actions' : 'View only'}
          </DropdownMenuItem>
        )}
        {actions.map((a) => (
          <DropdownMenuItem
            key={a.action}
            className={a.dangerLevel === 'Critical' ? 'text-red-600 focus:text-red-600' : undefined}
            onClick={() => onAction(nodeId, a)}
          >
            {LABELS[a.action] ?? a.action}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
