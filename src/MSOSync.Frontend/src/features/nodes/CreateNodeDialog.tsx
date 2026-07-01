import { useState, useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import type { Resolver } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { useQuery } from '@tanstack/react-query';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '../../components/ui/form';
import { Input } from '../../components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../components/ui/select';
import { Button } from '../../components/ui/button';
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { useCreateNodeMutation } from './mutations';
import { useTopologyGroups } from '../topology/hooks';
import { queryKeys } from '../../shared/queryKeys';
import { getNodes } from '../../shared/api/nodes';

const createNodeSchema = z
  .object({
    nodeId: z
      .string()
      .min(1, 'Node ID is required')
      .max(50)
      .regex(/^[a-zA-Z0-9_-]+$/, 'Only letters, numbers, hyphens, underscores'),
    groupId: z.string().min(1, 'Group is required'),
    syncUrl: z.string().url('Must be a valid URL'),
    heartbeatInterval: z.coerce.number().int().min(1).max(1440),
    transportMode: z.enum(['Pull', 'Push']),
    upstreamNodeId: z.string().optional(),
    dbServer: z.string().optional(),
    dbName: z.string().optional(),
    dbAuthMode: z.enum(['Windows', 'Sql']).optional(),
    dbUser: z.string().optional(),
    dbPassword: z.string().max(500).optional(),
  })
  .superRefine((data, ctx) => {
    if (data.dbAuthMode === 'Sql' && !data.dbUser) {
      ctx.addIssue({
        code: 'custom',
        path: ['dbUser'],
        message: 'Username required for SQL auth',
      });
    }
  });

type CreateNodeForm = z.infer<typeof createNodeSchema>;

const defaultValues: CreateNodeForm = {
  nodeId: '',
  groupId: '',
  syncUrl: 'http://',
  heartbeatInterval: 60,
  transportMode: 'Pull',
  upstreamNodeId: undefined,
  dbServer: undefined,
  dbName: undefined,
  dbAuthMode: undefined,
  dbUser: undefined,
  dbPassword: undefined,
};

interface CreateNodeDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function CreateNodeDialog({ open, onOpenChange }: CreateNodeDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const [tokenResult, setTokenResult] = useState<string | null>(null);

  const mutation = useCreateNodeMutation();
  const { data: groups } = useTopologyGroups();
  const { data: nodes } = useQuery({
    queryKey: queryKeys.nodes(),
    queryFn: getNodes,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });

  const form = useForm<CreateNodeForm>({
    resolver: zodResolver(createNodeSchema) as unknown as Resolver<CreateNodeForm>,
    defaultValues,
  });

  const dbAuthMode = form.watch('dbAuthMode');

  useEffect(() => {
    if (!open) {
      form.reset(defaultValues);
      setApiError(null);
      setTokenResult(null);
    }
  }, [open, form]);

  const onSubmit = async (values: CreateNodeForm) => {
    setApiError(null);
    try {
      const result = await mutation.mutateAsync({
        nodeId: values.nodeId,
        groupId: values.groupId,
        syncUrl: values.syncUrl,
        heartbeatInterval: values.heartbeatInterval,
        transportMode: values.transportMode,
        upstreamNodeId: values.upstreamNodeId || undefined,
        dbServer: values.dbServer || undefined,
        dbName: values.dbName || undefined,
        dbAuthMode: values.dbAuthMode || undefined,
        dbUser: values.dbUser || undefined,
        dbPassword: values.dbPassword || undefined,
      });
      setTokenResult(result.nodeToken);
      toast.success(`Node "${result.nodeId}" created`);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  return (
    <EntityDialog open={open} title="Add Node" onOpenChange={onOpenChange}>
      {tokenResult ? (
        <div className="flex flex-col gap-3">
          <div className="rounded-md bg-amber-50 dark:bg-amber-950 border border-amber-200 dark:border-amber-800 px-4 py-3">
            <p className="text-sm font-medium text-amber-800 dark:text-amber-200 mb-1">
              Node token — copy now, it will not be shown again
            </p>
            <div className="flex items-center gap-2">
              <code className="flex-1 text-xs font-mono break-all text-amber-900 dark:text-amber-100">
                {tokenResult}
              </code>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => {
                  void navigator.clipboard.writeText(tokenResult);
                  toast.success('Token copied');
                }}
              >
                Copy
              </Button>
            </div>
          </div>
          <div className="flex justify-end">
            <Button onClick={() => onOpenChange(false)}>Close</Button>
          </div>
        </div>
      ) : (
        <Form {...form}>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void form.handleSubmit(onSubmit)(e);
            }}
            className="flex flex-col gap-4"
          >
            {/* Node ID */}
            <FormField
              control={form.control}
              name="nodeId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Node ID</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="my-node-01" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* Node Group */}
            <FormField
              control={form.control}
              name="groupId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Node Group</FormLabel>
                  <Select onValueChange={field.onChange} value={field.value}>
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder="Select group…" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      {(groups ?? []).map((g) => (
                        <SelectItem key={g.groupId} value={g.groupId}>
                          {g.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* Sync URL */}
            <FormField
              control={form.control}
              name="syncUrl"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Sync URL</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="https://node.example.com" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* Heartbeat Interval */}
            <FormField
              control={form.control}
              name="heartbeatInterval"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Heartbeat Interval (minutes, 1–1440)</FormLabel>
                  <FormControl>
                    <Input {...field} inputMode="numeric" value={String(field.value)} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* Transport Mode */}
            <FormField
              control={form.control}
              name="transportMode"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Transport Mode</FormLabel>
                  <Select onValueChange={field.onChange} value={field.value}>
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder="Select mode…" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      <SelectItem value="Pull">Pull</SelectItem>
                      <SelectItem value="Push">Push</SelectItem>
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* Upstream Node (optional) */}
            <FormField
              control={form.control}
              name="upstreamNodeId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Upstream Node (optional)</FormLabel>
                  <Select
                    onValueChange={(v) => field.onChange(v === '__none__' ? undefined : v)}
                    value={field.value ?? '__none__'}
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder="None" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      <SelectItem value="__none__">None</SelectItem>
                      {(nodes ?? []).map((n) => (
                        <SelectItem key={n.nodeId} value={n.nodeId}>
                          {n.nodeId}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* Database Server (optional) */}
            <FormField
              control={form.control}
              name="dbServer"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Database Server (optional)</FormLabel>
                  <FormControl>
                    <Input {...field} value={field.value ?? ''} placeholder="localhost\SQLEXPRESS" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* Database Name (optional) */}
            <FormField
              control={form.control}
              name="dbName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Database Name (optional)</FormLabel>
                  <FormControl>
                    <Input {...field} value={field.value ?? ''} placeholder="MyDatabase" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* DB Auth Mode (optional) */}
            <FormField
              control={form.control}
              name="dbAuthMode"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>DB Auth Mode (optional)</FormLabel>
                  <Select
                    onValueChange={(v) => field.onChange(v === '__none__' ? undefined : v)}
                    value={field.value ?? '__none__'}
                  >
                    <FormControl>
                      <SelectTrigger>
                        <SelectValue placeholder="None" />
                      </SelectTrigger>
                    </FormControl>
                    <SelectContent>
                      <SelectItem value="__none__">None</SelectItem>
                      <SelectItem value="Windows">Windows</SelectItem>
                      <SelectItem value="Sql">SQL</SelectItem>
                    </SelectContent>
                  </Select>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* DB Username (optional, required when Sql) */}
            <FormField
              control={form.control}
              name="dbUser"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>
                    DB Username{dbAuthMode === 'Sql' ? '' : ' (optional)'}
                  </FormLabel>
                  <FormControl>
                    <Input {...field} value={field.value ?? ''} placeholder="sa" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            {/* DB Password (optional) */}
            <FormField
              control={form.control}
              name="dbPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>DB Password (optional)</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      type="password"
                      value={field.value ?? ''}
                      placeholder="••••••••"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormError error={apiError} />
            <FormActions
              loading={form.formState.isSubmitting}
              onCancel={() => onOpenChange(false)}
              submitLabel="Create Node"
            />
          </form>
        </Form>
      )}
    </EntityDialog>
  );
}
