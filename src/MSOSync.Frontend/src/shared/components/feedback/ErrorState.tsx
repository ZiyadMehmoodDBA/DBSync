import { Button } from '../../../components/ui/button';
import type { ApiError } from '../../types/common';

interface Props {
  error: unknown;
  onRetry?: () => void;
}

function extractMessage(error: unknown): string {
  if (error !== null && typeof error === 'object' && 'response' in error) {
    const response = (error as { response?: { data?: ApiError } }).response;
    if (response?.data?.detail) return response.data.detail;
    if (response?.data?.title) return response.data.title;
  }
  if (error instanceof Error) return error.message;
  return 'An unexpected error occurred';
}

export function ErrorState({ error, onRetry }: Props) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-12">
      <p className="text-sm text-red-600 dark:text-red-400">{extractMessage(error)}</p>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          Retry
        </Button>
      )}
    </div>
  );
}
