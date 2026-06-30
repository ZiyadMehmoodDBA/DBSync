import type { AxiosError } from 'axios';

interface ApiErrorBody {
  detail?: string;
  message?: string;
}

export function getErrorMessage(error: unknown): string {
  const axiosError = error as AxiosError<ApiErrorBody>;
  if (axiosError?.response?.data) {
    const { data } = axiosError.response;
    if (data.detail) return data.detail;
    if (data.message) return data.message;
  }
  if (error instanceof Error) return error.message;
  return 'An unexpected error occurred.';
}
