import type { AxiosError } from 'axios';

interface ApiErrorBody {
  detail?: string;
  message?: string;
}

function isAxiosError(error: unknown): error is AxiosError<ApiErrorBody> {
  return (
    typeof error === 'object' &&
    error !== null &&
    'response' in error &&
    typeof (error as Record<string, unknown>).response === 'object'
  );
}

export function getErrorMessage(error: unknown): string {
  if (isAxiosError(error)) {
    const { data } = error.response ?? {};
    if (data?.detail) return data.detail;
    if (data?.message) return data.message;
  }
  if (error instanceof Error) return error.message;
  return 'An unexpected error occurred.';
}
