import { Badge } from '@/components/ui/badge';
import { Loader2 } from 'lucide-react';
import type { OperationStatus, OperationResult } from '@/shared/types/operations';

interface Props {
  status: OperationStatus;
  result: OperationResult | null;
}

export function OperationStatusBadge({ status, result }: Props) {
  if (status === 'Running') {
    return (
      <Badge className="gap-1 bg-blue-100 text-blue-800 border border-blue-200">
        <Loader2 className="h-3 w-3 animate-spin" />
        Running
      </Badge>
    );
  }
  if (status === 'Pending') {
    return (
      <Badge className="bg-gray-100 text-gray-600 border border-gray-200">
        Pending
      </Badge>
    );
  }
  if (status === 'Cancelled') {
    return (
      <Badge className="bg-gray-100 text-gray-400 border border-gray-200 line-through">
        Cancelled
      </Badge>
    );
  }
  if (status === 'Paused') {
    return (
      <Badge className="bg-amber-100 text-amber-800 border border-amber-200">
        Paused
      </Badge>
    );
  }
  if (status === 'Failed') {
    return (
      <Badge className="bg-red-100 text-red-800 border border-red-200">
        Failed
      </Badge>
    );
  }
  // Completed — show result
  if (result === 'Success') {
    return (
      <Badge className="bg-green-100 text-green-800 border border-green-200">
        Success
      </Badge>
    );
  }
  if (result === 'PartialSuccess') {
    return (
      <Badge className="bg-yellow-100 text-yellow-800 border border-yellow-200">
        Partial
      </Badge>
    );
  }
  return (
    <Badge className="bg-gray-100 text-gray-600 border border-gray-200">
      {status}
    </Badge>
  );
}
