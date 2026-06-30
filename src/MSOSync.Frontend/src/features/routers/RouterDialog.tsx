import { useEffect, useState, useMemo } from 'react';
import { useForm } from 'react-hook-form';
import type { Resolver } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
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
import { createRouterSchema, updateRouterSchema, getDefaultValues, ROUTER_TYPES } from './schemas';
import type { CreateRouterForm } from './schemas';
import { useCreateRouterMutation, useUpdateRouterMutation } from './mutations';
import { useTopologyGroups } from '../topology/hooks';
import type { RouterDto } from '../../shared/types';

interface RouterDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: RouterDto;
  onOpenChange: (open: boolean) => void;
}

export function RouterDialog({ open, mode, initialValues, onOpenChange }: RouterDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const createMutation = useCreateRouterMutation();
  const updateMutation = useUpdateRouterMutation();
  const { data: groups } = useTopologyGroups();

  const schema = mode === 'create' ? createRouterSchema : updateRouterSchema;
  const defaultValues = useMemo(
    () => getDefaultValues(initialValues, mode),
    [initialValues, mode],
  );

  const form = useForm<CreateRouterForm>({
    resolver: zodResolver(schema) as unknown as Resolver<CreateRouterForm>,
    defaultValues: defaultValues as CreateRouterForm,
  });

  useEffect(() => {
    if (open) {
      form.reset(defaultValues as CreateRouterForm);
      setApiError(null);
    } else {
      form.reset();
      setApiError(null);
    }
  }, [open, defaultValues, form]);

  const onSubmit = async (values: CreateRouterForm) => {
    setApiError(null);
    try {
      if (mode === 'create') {
        await createMutation.mutateAsync({
          routerId: values.routerId,
          sourceNodeGroup: values.sourceNodeGroup,
          targetNodeGroup: values.targetNodeGroup,
          routerType: values.routerType,
        });
        toast.success('Router created');
      } else {
        if (!initialValues) return;
        await updateMutation.mutateAsync({
          routerId: initialValues.routerId,
          data: {
            sourceNodeGroup: values.sourceNodeGroup,
            targetNodeGroup: values.targetNodeGroup,
            routerType: values.routerType,
          },
        });
        toast.success('Router updated');
      }
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  const title = mode === 'create' ? 'Add Router' : `Edit Router: ${initialValues?.routerId ?? ''}`;

  return (
    <EntityDialog open={open} title={title} onOpenChange={onOpenChange}>
      <Form {...form}>
        <form
          onSubmit={(e) => { e.preventDefault(); void form.handleSubmit(onSubmit)(e); }}
          className="flex flex-col gap-4"
        >
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="routerId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Router ID</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="e.g. hub-to-spoke" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          <FormField
            control={form.control}
            name="sourceNodeGroup"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Source Node Group</FormLabel>
                <Select onValueChange={field.onChange} value={field.value}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue placeholder="Select source group…" />
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
            name="targetNodeGroup"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Target Node Group</FormLabel>
                <Select onValueChange={field.onChange} value={field.value}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue placeholder="Select target group…" />
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
            name="routerType"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Router Type</FormLabel>
                <Select onValueChange={field.onChange} value={field.value}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {ROUTER_TYPES.map((t) => (
                      <SelectItem key={t} value={t}>
                        {t}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel={mode === 'create' ? 'Create Router' : 'Save Changes'}
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
