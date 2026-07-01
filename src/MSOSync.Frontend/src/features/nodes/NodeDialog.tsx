import { useEffect, useState, useMemo } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import type { Resolver } from 'react-hook-form';
import { toast } from 'sonner';
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
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { updateNodeSchema, getDefaultValues } from './schemas';
import type { UpdateNodeForm } from './schemas';
import { useUpdateNodeMutation } from './mutations';
import { useTopologyGroups } from '../topology/hooks';
import type { NodeDto } from '../../shared/types';

interface NodeDialogProps {
  open: boolean;
  initialValues: NodeDto;
  onOpenChange: (open: boolean) => void;
}

export function NodeDialog({ open, initialValues, onOpenChange }: NodeDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const mutation = useUpdateNodeMutation();
  const { data: groups } = useTopologyGroups();

  const defaultValues = useMemo(
    () => getDefaultValues(initialValues),
    [initialValues],
  );

  const form = useForm<UpdateNodeForm>({
    resolver: zodResolver(updateNodeSchema) as unknown as Resolver<UpdateNodeForm>,
    defaultValues,
  });

  useEffect(() => {
    if (open) {
      form.reset(defaultValues);
      setApiError(null);
    } else {
      form.reset();
      setApiError(null);
    }
  }, [open, defaultValues, form]);

  const onSubmit = async (values: UpdateNodeForm) => {
    setApiError(null);
    try {
      await mutation.mutateAsync({ nodeId: initialValues.nodeId, data: values });
      toast.success('Node updated');
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  return (
    <EntityDialog
      open={open}
      title={`Edit Node: ${initialValues.nodeId}`}
      onOpenChange={onOpenChange}
    >
      <Form {...form}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void form.handleSubmit(onSubmit)(e);
          }}
          className="flex flex-col gap-4"
        >
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
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel="Save Changes"
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
