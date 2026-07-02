import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getPreferences,
  upsertPreference,
  deletePreference,
} from '../api/preferences';
import { queryKeys } from '../queryKeys';

export function usePreferences() {
  return useQuery({
    queryKey: queryKeys.userPreferences(),
    queryFn:  getPreferences,
    staleTime: Infinity,
  });
}

export function usePreference<T>(key: string, defaultValue: T): T {
  const { data } = usePreferences();
  if (data === undefined || !(key in data)) return defaultValue;
  return data[key] as T;
}

export function useSetPreference() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: unknown }) =>
      upsertPreference(key, value),
    onMutate: async ({ key, value }) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.userPreferences() });
      const previous = queryClient.getQueryData<Record<string, unknown>>(
        queryKeys.userPreferences(),
      );
      queryClient.setQueryData<Record<string, unknown>>(
        queryKeys.userPreferences(),
        old => ({ ...old, [key]: value }),
      );
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(queryKeys.userPreferences(), context.previous);
      }
    },
  });
}

export function useDeletePreference() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (key: string) => deletePreference(key),
    onMutate: async (key) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.userPreferences() });
      const previous = queryClient.getQueryData<Record<string, unknown>>(
        queryKeys.userPreferences(),
      );
      queryClient.setQueryData<Record<string, unknown>>(
        queryKeys.userPreferences(),
        (old) => {
          const next = { ...old };
          delete next[key];
          return next;
        },
      );
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(queryKeys.userPreferences(), context.previous);
      }
    },
  });
}
