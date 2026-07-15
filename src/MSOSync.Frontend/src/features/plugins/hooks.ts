import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { disablePlugin, enablePlugin, getPluginSummary, getPlugins } from './api';
import { queryKeys } from '../../shared/queryKeys';

export function usePlugins() {
  return useQuery({
    queryKey: queryKeys.plugins.all(),
    queryFn:  getPlugins,
  });
}

export function usePluginSummary() {
  return useQuery({
    queryKey: queryKeys.plugins.summary(),
    queryFn:  getPluginSummary,
  });
}

export function useEnablePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ pluginId }: { pluginId: string; name: string }) =>
      enablePlugin(pluginId),
    onSuccess: (_data, { name }) => {
      toast.info(`Plugin "${name}" enabled. Restart required to take effect.`);
      void qc.invalidateQueries({ queryKey: queryKeys.plugins.all() });
    },
    onError: () => toast.error('Failed to enable plugin.'),
  });
}

export function useDisablePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ pluginId }: { pluginId: string; name: string }) =>
      disablePlugin(pluginId),
    onSuccess: (_data, { name }) => {
      toast.info(`Plugin "${name}" disabled. Restart required to take effect.`);
      void qc.invalidateQueries({ queryKey: queryKeys.plugins.all() });
    },
    onError: () => toast.error('Failed to disable plugin.'),
  });
}
